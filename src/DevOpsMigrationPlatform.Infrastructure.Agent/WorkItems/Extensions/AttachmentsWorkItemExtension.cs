// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Work-item capability: replays attachment binaries onto the target work item during import. The
/// attachment-replay business rule, expressed as an <see cref="IModuleExtension"/> port, independent of
/// the checkpoint/resume delivery mechanism that drives it.
/// </summary>
public sealed class AttachmentsWorkItemExtension : IModuleExtension
{
    private readonly AttachmentsExtensionOptions _options;

    public AttachmentsWorkItemExtension(IOptions<AttachmentsExtensionOptions> options)
        => _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    public string Module => "WorkItems";
    public string Name => "Attachments";
    public int Order => 400;
    public bool SupportsExport => false;
    public bool SupportsImport => true;
    public bool IsEnabled => _options.Enabled;

    public Task ExportAsync(IExtensionContext context, CancellationToken ct) => Task.CompletedTask;

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

        var replayService = new AttachmentReplayService(
            ctx.Target,
            ctx.IdMapStore,
            NullLogger<AttachmentReplayService>.Instance);

        await replayService.ReplayAsync(
            ctx.Revision,
            ctx.FolderPath,
            ctx.TargetWorkItemId,
            ctx.ReadBinaryAsync,
            ctx.AvailableBinaryPaths,
            ct).ConfigureAwait(false);
    }
}
