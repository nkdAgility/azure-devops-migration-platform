// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Replays revision attachments to the target system and persists source-to-target attachment mappings.
/// </summary>
public sealed class AttachmentReplayService
{
    private readonly IWorkItemImportTarget _target;
    private readonly IIdMapStore _idMapStore;
    private readonly ILogger<AttachmentReplayService> _logger;

    public AttachmentReplayService(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ILogger<AttachmentReplayService> logger)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _idMapStore = idMapStore ?? throw new ArgumentNullException(nameof(idMapStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ReplayAsync(
        WorkItemRevision revision,
        string folderPath,
        int targetWorkItemId,
        Func<string, CancellationToken, Task<Stream?>> readBinaryAsync,
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
            var existingId = await _idMapStore.GetAttachmentIdAsync(
                revision.WorkItemId, revision.RevisionIndex, attachment.RelativePath, ct).ConfigureAwait(false);

            if (existingId is not null)
            {
                _logger.LogDebug("[WorkItems] Attachment {File} already uploaded — skipping.", attachment.RelativePath);
                continue;
            }

            var binaryPath = $"{folderPath}/{attachment.RelativePath}";
            using var stream = await readBinaryAsync(binaryPath, ct).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("[WorkItems] Attachment binary {Path} not found — skipping.", binaryPath);
                continue;
            }

            var targetAttachmentId = await _target.UploadAttachmentAsync(
                targetWorkItemId, attachment.OriginalName, stream, ct).ConfigureAwait(false);

            await _idMapStore.SetAttachmentMappingAsync(
                revision.WorkItemId, revision.RevisionIndex, attachment.RelativePath, targetAttachmentId, ct)
                .ConfigureAwait(false);
        }
    }
}
