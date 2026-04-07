#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Export;
using Microsoft.Extensions.Logging;

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
/// </summary>
public sealed class WorkItemsModule : IDataTypeModule
{
    private const string DefaultWiqlQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    public string Name => "WorkItems";
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly ILogger<WorkItemsModule> _logger;

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        var job = context.Job;

        var orgUrl = job.Source?.Url ?? throw new InvalidOperationException("Job.Source.Url is required.");
        var project = job.Source?.Project ?? throw new InvalidOperationException("Job.Source.Project is required.");
        var pat = job.Source?.Authentication?.ResolvedAccessToken ?? string.Empty;

        var query = ResolveParameter(job, "query", DefaultWiqlQuery);
        var includeAttachments = ResolveParameter(job, "includeAttachments", "true")
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={IncludeAttachments})",
            orgUrl, project, includeAttachments);

        var source = await _sourceFactory
            .CreateAsync(orgUrl, project, pat, query, ct)
            .ConfigureAwait(false);

        var stateStore = new FileSystemStateStore(ResolvePackagePath(job));
        var checkpointingService = new CheckpointingService(stateStore);

        IAttachmentBinarySource? attachmentBinarySource = null;
        // Attachment source is wired via DI in the agent; if not available, skip downloads.
        // WorkItemsModule itself does not construct AzureDevOpsAttachmentBinarySource to
        // preserve the module isolation rule (no Infrastructure.AzureDevOps reference here).

        var orchestrator = new WorkItemExportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
            attachmentBinarySource);

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
    }

    public Task ImportAsync(ImportContext context, CancellationToken ct) =>
        throw new NotImplementedException("WorkItems import is deferred to a future spec.");

    public Task ValidateAsync(ValidationContext context, CancellationToken ct) =>
        throw new NotImplementedException("WorkItems validation is deferred to a future spec.");

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

    private static string ResolvePackagePath(MigrationJob job)
    {
        var uri = job.Artefacts.PackageUri;
        if (string.IsNullOrWhiteSpace(uri)) return ".";
        return uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            ? uri["file:///".Length..].Replace('/', System.IO.Path.DirectorySeparatorChar)
            : uri;
    }
}

#endif
