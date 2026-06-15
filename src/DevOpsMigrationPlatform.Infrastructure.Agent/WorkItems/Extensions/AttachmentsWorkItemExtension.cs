// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Work-item capability: replays attachment binaries onto the target work item during import. The
/// attachment-replay business rule, expressed as an <see cref="IModuleExtension"/> port, independent of
/// the checkpoint/resume delivery mechanism that drives it.
/// When an <see cref="IAttachmentBinarySource"/> is injected, this extension also supports export:
/// it downloads attachment binaries from the source system and writes them into the migration package.
/// </summary>
public sealed class AttachmentsWorkItemExtension : IModuleExtension
{
    private readonly AttachmentsExtensionOptions _options;
    private readonly ILogger<AttachmentReplayTool> _logger;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;

    public AttachmentsWorkItemExtension(
        IOptions<AttachmentsExtensionOptions> options,
        ILogger<AttachmentReplayTool> logger,
        IAttachmentBinarySource? attachmentBinarySource = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _attachmentBinarySource = attachmentBinarySource;
    }

    public string Module => "WorkItems";
    public string Name => "Attachments";
    public int Order => 400;
    public bool SupportsExport => _attachmentBinarySource is not null && _options.Enabled;
    public bool SupportsImport => true;
    public bool IsEnabled => _options.Enabled;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not WorkItemRevisionExportContext ctx)
            throw new ArgumentException($"Expected {nameof(WorkItemRevisionExportContext)}.", nameof(context));

        if (_attachmentBinarySource == null)
            return;

        var streamingSource = _attachmentBinarySource as IStreamingAttachmentBinarySource;

        foreach (var attachment in ctx.Revision.Attachments)
        {
            var downloadUrl = attachment.DownloadUrl;
            var attachmentName = attachment.OriginalName is { Length: > 0 } n ? n : attachment.RelativePath ?? "(unknown)";
            var targetPath = $"{ctx.FolderPath}{attachment.RelativePath ?? string.Empty}";

            _logger.LogDebug("[Attachments] WI {WorkItemId} rev {RevisionIndex}: downloading '{Name}'.",
                ctx.WorkItemId, ctx.RevisionIndex, attachmentName);

            bool downloadSucceeded;

            if (streamingSource != null)
            {
                var revisionFolder = GetRevisionFolderPath(ctx.FolderPath.TrimEnd('/'));
                var result = await streamingSource
                    .StreamToStoreAsync(
                        ctx.WorkItemId,
                        ctx.RevisionIndex,
                        attachment,
                        ctx.Package,
                        ctx.Organisation,
                        ctx.ProjectName,
                        revisionFolder,
                        ct)
                    .ConfigureAwait(false);

                downloadSucceeded = result.HasValue;
            }
            else
            {
                var bytes = await _attachmentBinarySource
                    .GetBytesAsync(ctx.WorkItemId, ctx.RevisionIndex, attachment, ct)
                    .ConfigureAwait(false);

                if (bytes != null)
                {
                    var fileName = GetFileName(targetPath);
                    var revisionFolder = GetRevisionFolderPath(targetPath);
                    using var stream = new MemoryStream(bytes, writable: false);
                    await ctx.Package.PersistContentStreamAsync(
                        new PackageContentContext(
                            PackageContentKind.Artefact,
                            Organisation: ctx.Organisation,
                            Project: ctx.ProjectName,
                            Module: "WorkItems",
                            Address: new WorkItemAttachmentAddress(revisionFolder, fileName)),
                        stream,
                        "application/octet-stream",
                        ct).ConfigureAwait(false);
                    downloadSucceeded = true;
                }
                else
                {
                    downloadSucceeded = false;
                }
            }

            if (downloadSucceeded)
            {
                _logger.LogInformation("[Attachments] WI {WorkItemId} rev {RevisionIndex}: downloaded '{Name}'.",
                    ctx.WorkItemId, ctx.RevisionIndex, attachmentName);
            }
            else
            {
                _logger.LogWarning("[Attachments] WI {WorkItemId} rev {RevisionIndex}: failed to download '{Name}'.",
                    ctx.WorkItemId, ctx.RevisionIndex, attachmentName);
            }
        }
    }

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not WorkItemExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(WorkItemExtensionContext)}.", nameof(context));
        if (ctx.Target is null)
            throw new ArgumentException("WorkItemExtensionContext.Target is required for import.", nameof(context));
        if (ctx.IdMapStore is null)
            throw new ArgumentException("WorkItemExtensionContext.IdMapStore is required for attachment import.", nameof(context));
        if (ctx.ReadBinaryAsync is null)
            throw new ArgumentException("WorkItemExtensionContext.ReadBinaryAsync is required for attachment import.", nameof(context));

        var replayTool = new AttachmentReplayTool(
            ctx.Target,
            ctx.IdMapStore,
            _logger);

        await replayTool.ReplayAsync(
            ctx.Revision,
            ctx.FolderPath,
            ctx.TargetWorkItemId,
            ctx.ReadBinaryAsync,
            ctx.AvailableBinaryPaths,
            ct).ConfigureAwait(false);
    }

    private static string GetRevisionFolderPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("WorkItems/".Length);

        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized.Substring(0, lastSlash) : normalized;
    }

    private static string GetFileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
    }
}
