#if !NET481
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Export;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// <see cref="IModule"/> implementation for work item export/import.
/// Streams revisions from <see cref="IWorkItemRevisionSourceFactory"/>, writes each
/// revision folder via <see cref="WorkItemExportOrchestrator"/>, and streams attachment
/// binaries beside <c>revision.json</c>.
///
/// Design guarantee: processes one <see cref="WorkItemRevision"/> at a time via
/// <see cref="IAsyncEnumerable{T}"/>; streams attachment binaries directly to
/// <see cref="IArtefactStore.WriteBinaryAsync"/>; no revision list or attachment byte
/// array is accumulated in memory.
/// 
/// Inline comment fetching is gated by the Comments extension Enabled flag.
/// </summary>
public sealed class WorkItemsModule : IModule
{
    public string Name => "WorkItems";
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly ILogger<WorkItemsModule> _logger;

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        var job = context.Job;

        var orgUrl = job.Source?.ResolvedUrl ?? throw new InvalidOperationException("Job.Source.Url is required.");
        var project = job.Source?.Project ?? throw new InvalidOperationException("Job.Source.Project is required.");
        var pat = job.Source?.Authentication?.ResolvedAccessToken ?? string.Empty;

        var workItemsModule = job.Modules
            ?.FirstOrDefault(m => string.Equals(m.Name, "WorkItems", StringComparison.OrdinalIgnoreCase));

        var ext = workItemsModule is not null
            ? WorkItemsModuleExtensions.FromModule(workItemsModule)
            : new WorkItemsModuleExtensions();

        _logger.LogInformation(
            "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={AttachmentsEnabled}, comments={CommentsEnabled})",
            orgUrl, project, ext.AttachmentsEnabled, ext.Comments.Enabled);

        var source = await _sourceFactory
            .CreateAsync(orgUrl, project, pat, ext.Query, ct)
            .ConfigureAwait(false);

        var checkpointingService = new CheckpointingService(context.StateStore);

        // Comments extension gates inline comment fetching.
        var inlineFactory = ext.Comments.Enabled ? _inlineCommentSourceFactory : null;

        var orchestrator = new WorkItemExportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
            ext.AttachmentsEnabled ? _attachmentBinarySource : null,
            context.ProgressSink,
            organisationUrl: orgUrl,
            project: project,
            pat: pat,
            inlineCommentSourceFactory: inlineFactory);

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
    }

    public Task ImportAsync(ImportContext context, CancellationToken ct) =>
        throw new NotSupportedException("WorkItems import is not yet supported.");

    public Task ValidateAsync(ValidationContext context, CancellationToken ct) =>
        throw new NotSupportedException("WorkItems validation is not yet supported.");

}

#endif
