// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using AttachmentUploadResult = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.AttachmentUploadResult;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;

/// <summary>
/// Replays revision attachments to the target system and persists source-to-target attachment mappings.
/// </summary>
public sealed class AttachmentReplayTool
{
    private readonly IWorkItemTarget _target;
    private readonly IIdMapStore _idMapStore;
    private readonly ILogger<AttachmentReplayTool> _logger;

    public AttachmentReplayTool(
        IWorkItemTarget target,
        IIdMapStore idMapStore,
        ILogger<AttachmentReplayTool> logger)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _idMapStore = idMapStore ?? throw new ArgumentNullException(nameof(idMapStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Uploads all attachment binaries for the revision and returns the results.
    /// Does NOT add the attachment relations to the work item — the caller is responsible
    /// for passing the returned list to <c>ApplyRevisionAsync</c> to do that in one PATCH.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentUploadResult>> UploadBinariesAsync(
        WorkItemRevision revision,
        string folderPath,
        int targetWorkItemId,
        Func<string, CancellationToken, Task<Stream?>> readBinaryAsync,
        ISet<string>? availableBinaryPaths,
        CancellationToken ct)
    {
        if (revision is null)
            throw new ArgumentNullException(nameof(revision));
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(folderPath));
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
        if (readBinaryAsync is null)
            throw new ArgumentNullException(nameof(readBinaryAsync));

        var results = new List<AttachmentUploadResult>();

        foreach (var attachment in revision.Attachments)
        {
            if (!TryBuildReplayAttachment(folderPath, attachment, availableBinaryPaths, out var replayAttachment))
                continue;

            var existingId = await _idMapStore.GetAttachmentIdAsync(
                revision.WorkItemId, revision.RevisionIndex, replayAttachment.RelativePath, ct).ConfigureAwait(false);

            if (existingId is not null)
            {
                _logger.LogDebug("[WorkItems] Attachment {File} already uploaded — including existing URL in results.", replayAttachment.RelativePath);
                results.Add(new AttachmentUploadResult(existingId, replayAttachment.OriginalName));
                continue;
            }

            var binaryPath = replayAttachment.BinaryPath;
            using var stream = await readBinaryAsync(binaryPath, ct).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("[WorkItems] Attachment binary {Path} not found — skipping.", binaryPath);
                continue;
            }

            var targetAttachmentUrl = await _target.UploadAttachmentAsync(
                targetWorkItemId, replayAttachment.OriginalName, stream, ct).ConfigureAwait(false);

            await _idMapStore.SetAttachmentMappingAsync(
                revision.WorkItemId, revision.RevisionIndex, replayAttachment.RelativePath, targetAttachmentUrl, ct)
                .ConfigureAwait(false);

            results.Add(new AttachmentUploadResult(targetAttachmentUrl, replayAttachment.OriginalName));
        }

        return results;
    }

    /// <summary>
    /// Uploads all attachment binaries and immediately adds the attachment relations to the work item.
    /// Kept for backward-compatibility with callers that do not use <c>ApplyRevisionAsync</c>.
    /// </summary>
    public async Task ReplayAsync(
        WorkItemRevision revision,
        string folderPath,
        int targetWorkItemId,
        Func<string, CancellationToken, Task<Stream?>> readBinaryAsync,
        ISet<string>? availableBinaryPaths,
        CancellationToken ct)
    {
        if (revision is null)
            throw new ArgumentNullException(nameof(revision));
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(folderPath));
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
        if (readBinaryAsync is null)
            throw new ArgumentNullException(nameof(readBinaryAsync));

        foreach (var attachment in revision.Attachments)
        {
            if (!TryBuildReplayAttachment(folderPath, attachment, availableBinaryPaths, out var replayAttachment))
                continue;

            var existingId = await _idMapStore.GetAttachmentIdAsync(
                revision.WorkItemId, revision.RevisionIndex, replayAttachment.RelativePath, ct).ConfigureAwait(false);

            if (existingId is not null)
            {
                _logger.LogDebug("[WorkItems] Attachment {File} already uploaded — skipping.", replayAttachment.RelativePath);
                continue;
            }

            var binaryPath = replayAttachment.BinaryPath;
            using var stream = await readBinaryAsync(binaryPath, ct).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("[WorkItems] Attachment binary {Path} not found — skipping.", binaryPath);
                continue;
            }

            var targetAttachmentId = await _target.UploadAttachmentAsync(
                targetWorkItemId, replayAttachment.OriginalName, stream, ct).ConfigureAwait(false);

            await _idMapStore.SetAttachmentMappingAsync(
                revision.WorkItemId, revision.RevisionIndex, replayAttachment.RelativePath, targetAttachmentId, ct)
                .ConfigureAwait(false);
        }
    }

    private bool TryBuildReplayAttachment(
        string folderPath,
        Abstractions.Agent.Attachments.AttachmentMetadata attachment,
        ISet<string>? availableBinaryPaths,
        out ReplayAttachment replayAttachment)
    {
        replayAttachment = default;

        if (string.IsNullOrWhiteSpace(attachment.RelativePath))
        {
            _logger.LogWarning("[WorkItems] Attachment metadata is missing relativePath — skipping.");
            return false;
        }

        var originalName = !string.IsNullOrWhiteSpace(attachment.OriginalName)
            ? attachment.OriginalName
            : Path.GetFileName(attachment.RelativePath);
        if (string.IsNullOrWhiteSpace(originalName))
        {
            _logger.LogWarning("[WorkItems] Attachment {Path} has no name metadata — skipping.", attachment.RelativePath);
            return false;
        }

        var binaryPath = $"{folderPath}/{attachment.RelativePath}".Replace('\\', '/');
        var relativeToWorkItems = binaryPath.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase)
            ? binaryPath.Substring("WorkItems/".Length)
            : binaryPath;
        if (availableBinaryPaths is not null &&
            !availableBinaryPaths.Contains(binaryPath) &&
            !availableBinaryPaths.Contains(relativeToWorkItems))
        {
            _logger.LogWarning("[WorkItems] Attachment binary {Path} was not found in revision enumeration — skipping.", binaryPath);
            return false;
        }

        replayAttachment = new ReplayAttachment(
            attachment.RelativePath,
            originalName,
            binaryPath);
        return true;
    }

    private readonly record struct ReplayAttachment(
        string RelativePath,
        string OriginalName,
        string BinaryPath);
}
