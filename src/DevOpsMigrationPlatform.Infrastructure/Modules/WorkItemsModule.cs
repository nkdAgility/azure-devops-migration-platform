#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// <see cref="IDataTypeModule"/> implementation for work item export/import.
/// Streams revisions from <see cref="IWorkItemRevisionSourceFactory"/>, writes each
/// revision folder via <see cref="WorkItemExportOrchestrator"/>, and streams attachment
/// binaries beside <c>revision.json</c>.
///
/// Design guarantee: processes one <see cref="WorkItemRevision"/> at a time via
/// <see cref="IAsyncEnumerable{T}"/>; streams attachment binaries directly to
/// <see cref="IArtefactStore.WriteBinaryAsync"/>; no revision list or attachment byte
/// array is accumulated in memory.
/// 
/// Also orchestrates comment export and embedded image download after revision export
/// via <see cref="IWorkItemCommentExportService"/> and <see cref="IEmbeddedImageExportService"/>.
/// </summary>
public sealed class WorkItemsModule : IDataTypeModule
{
    private const string DefaultWiqlQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    public string Name => "WorkItems";
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentExportService? _commentExportService;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly ILogger<WorkItemsModule> _logger;

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentExportService? commentExportService = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _attachmentBinarySource = attachmentBinarySource;
        _commentExportService = commentExportService;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        var job = context.Job;

        var orgUrl = job.Source?.ResolvedUrl ?? throw new InvalidOperationException("Job.Source.Url is required.");
        var project = job.Source?.Project ?? throw new InvalidOperationException("Job.Source.Project is required.");
        var pat = job.Source?.Authentication?.ResolvedAccessToken ?? string.Empty;

        var query = ResolveParameter(job, "query", DefaultWiqlQuery);
        var includeAttachments = ResolveParameter(job, "includeAttachments", "true")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
        var inlineCommentsEnabled = ResolveParameter(job, "inlineComments.enabled", "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={IncludeAttachments}, inlineComments={InlineCommentsEnabled})",
            orgUrl, project, includeAttachments, inlineCommentsEnabled);

        var source = await _sourceFactory
            .CreateAsync(orgUrl, project, pat, query, ct)
            .ConfigureAwait(false);

        var checkpointingService = new CheckpointingService(context.StateStore);

        // Only activate inline comment fetching when the user has explicitly opted in.
        // Defaults to false to avoid unexpected API calls in standard export runs.
        var inlineFactory = inlineCommentsEnabled ? _inlineCommentSourceFactory : null;

        var orchestrator = new WorkItemExportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
            _attachmentBinarySource,
            context.ProgressSink,
            _commentExportService,
            orgUrl,
            project,
            pat,
            inlineFactory);

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
    }

    public Task ImportAsync(ImportContext context, CancellationToken ct) =>
        throw new NotSupportedException("WorkItems import is not yet supported.");

    public Task ValidateAsync(ValidationContext context, CancellationToken ct) =>
        throw new NotSupportedException("WorkItems validation is not yet supported.");

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string ResolveParameter(MigrationJob job, string key, string defaultValue)
    {
        if (job.Modules is null) return defaultValue;
        foreach (var module in job.Modules)
        {
            if (!string.Equals(module.Name, "WorkItems", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var scope in module.Scopes)
            {
                if (scope.Parameters.TryGetValue(key, out var raw) && raw is not null)
                    return raw.ToString() ?? defaultValue;
            }
        }
        return defaultValue;
    }
}

#endif
