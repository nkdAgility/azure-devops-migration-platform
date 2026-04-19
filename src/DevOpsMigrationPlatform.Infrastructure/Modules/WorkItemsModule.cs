#if !NET481
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Export;
using DevOpsMigrationPlatform.Infrastructure.Import;
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
    public IReadOnlyList<string> DependsOn => new[] { "Identities" };

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly IWorkItemImportTargetFactory _importTargetFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly IIdentityMappingService _identityMappingService;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IRevisionFolderProcessorFactory _processorFactory;
    private readonly ILogger<WorkItemsModule> _logger;
    private readonly ILogger<WorkItemImportOrchestrator> _orchestratorLogger;
    private readonly DevOpsMigrationPlatform.Abstractions.Services.IWorkItemFetchService? _fetchService;

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger,
        ILogger<WorkItemImportOrchestrator> orchestratorLogger,
        IWorkItemImportTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        IIdentityMappingService identityMappingService,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IRevisionFolderProcessorFactory processorFactory,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        DevOpsMigrationPlatform.Abstractions.Services.IWorkItemFetchService? fetchService = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orchestratorLogger = orchestratorLogger ?? throw new ArgumentNullException(nameof(orchestratorLogger));
        _importTargetFactory = importTargetFactory ?? throw new ArgumentNullException(nameof(importTargetFactory));
        _resolutionStrategyFactory = resolutionStrategyFactory ?? throw new ArgumentNullException(nameof(resolutionStrategyFactory));
        _identityMappingService = identityMappingService ?? throw new ArgumentNullException(nameof(identityMappingService));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _idMapStoreFactory = idMapStoreFactory ?? throw new ArgumentNullException(nameof(idMapStoreFactory));
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _fetchService = fetchService;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        var job = context.Job;

        var endpointOptions = job.Source ?? throw new InvalidOperationException("Job.Source is required for export.");
        var orgUrl = endpointOptions.GetResolvedUrl();
        var project = endpointOptions.GetProject();

        var workItemsModule = job.Modules
            ?.FirstOrDefault(m => string.Equals(m.Name, "WorkItems", StringComparison.OrdinalIgnoreCase));

        var ext = workItemsModule is not null
            ? WorkItemsModuleExtensions.FromModule(workItemsModule)
            : new WorkItemsModuleExtensions();

        _logger.LogInformation(
            "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={AttachmentsEnabled}, comments={CommentsEnabled})",
            orgUrl, project, ext.AttachmentsEnabled, ext.Comments.Enabled);

        var source = await _sourceFactory
            .CreateAsync(endpointOptions, ct)
            .ConfigureAwait(false);

        var checkpointingService = _checkpointingFactory.Create(context.StateStore);

        // Comments extension gates inline comment fetching.
        var inlineFactory = ext.Comments.Enabled ? _inlineCommentSourceFactory : null;

        // Build combined filter options (include as Regex + exclude as NotRegex).
        var allFilters = ext.IncludeFilters.Concat(ext.ExcludeFilters).ToList();

        var orchestrator = new WorkItemExportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
            ext.AttachmentsEnabled ? _attachmentBinarySource : null,
            context.ProgressSink,
            endpoint: endpointOptions,
            project: project,
            inlineCommentSourceFactory: inlineFactory,
            fetchService: allFilters.Count > 0 ? _fetchService : null,
            filterOptions: allFilters.Count > 0 ? allFilters : null);

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
    }

    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        var job = context.Job;

        var targetJob = job.Target ?? throw new InvalidOperationException("Job.Target is required for import.");
        var orgUrl = targetJob.GetResolvedUrl();
        var project = targetJob.GetProject();

        var endpointOptions = targetJob;

        var workItemsModule = job.Modules
            ?.FirstOrDefault(m => string.Equals(m.Name, "WorkItems", StringComparison.OrdinalIgnoreCase));

        var ext = workItemsModule is not null
            ? WorkItemsModuleExtensions.FromModule(workItemsModule)
            : new WorkItemsModuleExtensions();

        _logger.LogInformation(
            "[WorkItems] Importing into {OrgUrl}/{Project} (revisions={Revisions}, links={Links}, attachments={Attachments}, comments={Comments})",
            orgUrl, project, ext.RevisionsEnabled, ext.LinksEnabled, ext.AttachmentsEnabled, ext.Comments.Enabled);

        var target = await _importTargetFactory.CreateAsync(endpointOptions, ct).ConfigureAwait(false);
        var checkpointingService = _checkpointingFactory.Create(context.StateStore);

        // Resolve the strategy at execution time — the factory creates the correct implementation
        // based on the module config and target connection parameters.
        var resolutionStrategy = await _resolutionStrategyFactory
            .CreateAsync(ext.ResolutionStrategy, target, endpointOptions, ct)
            .ConfigureAwait(false);

        // Derive the SQLite idmap.db path from the package URI
        var dbFilePath = ResolveIdMapPath(job.Artefacts.PackageUri);
        var idMapStore = _idMapStoreFactory.Create(dbFilePath);

        var processor = _processorFactory.Create(
            target,
            idMapStore,
            checkpointingService,
            _identityMappingService,
            context.ArtefactStore);

        // Build combined filter options for import (include as Regex + exclude as NotRegex).
        var importFilters = ext.IncludeFilters.Concat(ext.ExcludeFilters).ToList();

        var orchestrator = new WorkItemImportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
            context.ProgressSink,
            resolutionStrategy,
            idMapStore,
            processor,
            target,
            _orchestratorLogger,
            filterOptions: importFilters.Count > 0 ? importFilters : null);

        var resumeMode = job.Resume?.Mode ?? ResumeMode.Auto;
        await orchestrator.ImportAsync(ext, resumeMode, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Import complete.");
    }

    private static string ResolveIdMapPath(string packageUri)
    {
        string localRoot;
        if (packageUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            localRoot = packageUri["file:///".Length..].Replace('/', Path.DirectorySeparatorChar);
        else
            localRoot = packageUri;

        return Path.Combine(localRoot, "Checkpoints", "idmap.db");
    }

    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var job = context.Job;
        var mode = job.Mode ?? "Export";

        // Only perform package-side validation for Import or Both modes
        if (!string.Equals(mode, "Import", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase))
            return;

        // Tier 2: Verify the WorkItems/ prefix has at least one revision folder
        var found = false;
        await foreach (var path in context.ArtefactStore.EnumerateAsync("WorkItems/", ct).ConfigureAwait(false))
        {
            found = true;
            break;
        }

        if (!found)
        {
            context.Errors.Add(new ValidationError
            {
                Path = "WorkItems/",
                Message = "The package contains no work item revision folders under WorkItems/. " +
                          "Ensure an export has been run before attempting import."
            });
        }
    }

}

#endif
