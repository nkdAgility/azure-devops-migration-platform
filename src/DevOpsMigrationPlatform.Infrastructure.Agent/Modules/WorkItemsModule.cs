#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

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

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly IWorkItemImportTargetFactory _importTargetFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IRevisionFolderProcessorFactory _processorFactory;
    private readonly ILogger<WorkItemsModule> _logger;
    private readonly ILogger<WorkItemImportOrchestrator> _orchestratorLogger;
    private readonly IWorkItemFetchService? _fetchService;
    private readonly IMigrationMetrics? _metrics;
    private readonly IWorkItemDiscoveryService? _discoveryService;
    private readonly IExportProgressStoreFactory? _exportProgressStoreFactory;
    private readonly IClassificationTreeCapture? _classificationTreeCapture;
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly INodeEnsurer? _nodeEnsurer;

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger,
        ILogger<WorkItemImportOrchestrator> orchestratorLogger,
        IWorkItemImportTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IRevisionFolderProcessorFactory processorFactory,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        IWorkItemFetchService? fetchService = null,
        IMigrationMetrics? metrics = null,
        IWorkItemDiscoveryService? discoveryService = null,
        IExportProgressStoreFactory? exportProgressStoreFactory = null,
        IClassificationTreeCapture? classificationTreeCapture = null,
        IReferencedPathTracker? referencedPathTracker = null,
        INodeEnsurer? nodeEnsurer = null,
        IIdentityLookupTool? identityLookupTool = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orchestratorLogger = orchestratorLogger ?? throw new ArgumentNullException(nameof(orchestratorLogger));
        _importTargetFactory = importTargetFactory ?? throw new ArgumentNullException(nameof(importTargetFactory));
        _resolutionStrategyFactory = resolutionStrategyFactory ?? throw new ArgumentNullException(nameof(resolutionStrategyFactory));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _idMapStoreFactory = idMapStoreFactory ?? throw new ArgumentNullException(nameof(idMapStoreFactory));
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _fetchService = fetchService;
        _metrics = metrics;
        _discoveryService = discoveryService;
        _exportProgressStoreFactory = exportProgressStoreFactory;
        _classificationTreeCapture = classificationTreeCapture;
        _referencedPathTracker = referencedPathTracker;
        _nodeEnsurer = nodeEnsurer;
        _identityLookupTool = identityLookupTool;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("workitems.export");

        var job = context.Job;

        var endpointOptions = job.Source ?? throw new InvalidOperationException("Job.Source is required for export.");
        var orgUrl = endpointOptions.GetResolvedUrl();
        var project = endpointOptions.GetProject();

        var workItemsModule = job.Modules
            ?.FirstOrDefault(m => string.Equals(m.Name, "WorkItems", StringComparison.OrdinalIgnoreCase));

        var ext = workItemsModule is not null
            ? WorkItemsModuleExtensions.FromModule(workItemsModule)
            : new WorkItemsModuleExtensions();

        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation(
                "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={AttachmentsEnabled}, comments={CommentsEnabled})",
                orgUrl, project, ext.AttachmentsEnabled, ext.Comments.Enabled);
        }

        // Option C — warn when config enables a feature but the backing service is absent.
        if (ext.AttachmentsEnabled && _attachmentBinarySource == null)
            _logger.LogWarning("[WorkItems] AttachmentsEnabled is true but no IAttachmentBinarySource is registered — attachment binaries will NOT be written to the package. Register a connector-specific IAttachmentBinarySource to enable attachment export.");
        if (ext.Comments.Enabled && _inlineCommentSourceFactory == null)
            _logger.LogWarning("[WorkItems] Comments.Enabled is true but no IWorkItemCommentSourceFactory is registered — inline comments will NOT be exported. Register a connector-specific IWorkItemCommentSourceFactory to enable comment export.");

        // Build combined filter options (include as Regex + exclude as NotRegex).
        var allFilters = ext.IncludeFilters.Concat(ext.ExcludeFilters).ToList();

        if (allFilters.Count > 0 && _fetchService == null)
            _logger.LogWarning("[WorkItems] IncludeFilters/ExcludeFilters are configured but no IWorkItemFetchService is registered — filters will be ignored and all work items will be exported. Register a connector-specific IWorkItemFetchService to enable filtered export.");

        var source = await _sourceFactory
            .CreateAsync(endpointOptions, ct)
            .ConfigureAwait(false);

        if (_referencedPathTracker is null)
            _logger.LogWarning("[WorkItems] IReferencedPathTracker is not available — referenced path tracking will be skipped.");

        if (_classificationTreeCapture is null)
            _logger.LogWarning("[WorkItems] IClassificationTreeCapture is not available — source-tree.json will not be written.");

        if (_referencedPathTracker is not null)
            await _referencedPathTracker.InitializeAsync(context.ArtefactStore, ct).ConfigureAwait(false);
        if (_classificationTreeCapture is not null)
            _ = await _classificationTreeCapture.CaptureAsync(context.ArtefactStore, endpointOptions, ct, _metrics, job.JobId).ConfigureAwait(false);

        var checkpointingService = _checkpointingFactory.Create(context.StateStore);

        // Comments extension gates inline comment fetching.
        var inlineFactory = ext.Comments.Enabled ? _inlineCommentSourceFactory : null;

        var orchestrator = new WorkItemExportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
            ext.AttachmentsEnabled ? _attachmentBinarySource : null,
            context.ProgressSink,
            endpoint: endpointOptions,
            project: project,
            inlineCommentSourceFactory: inlineFactory,
            fetchService: allFilters.Count > 0 ? _fetchService : null,
            filterOptions: allFilters.Count > 0 ? allFilters : null,
            metrics: _metrics,
            jobId: job.JobId,
            logger: _logger,
            wiqlQuery: ext.Query,
            discoveryService: _discoveryService,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: job.Package.PackageUri,
            referencedPathTracker: _referencedPathTracker);

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
    }

    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("workitems.import");

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

        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation(
                "[WorkItems] Importing into {OrgUrl}/{Project} (revisions={Revisions}, links={Links}, attachments={Attachments}, comments={Comments})",
                orgUrl, project, ext.RevisionsEnabled, ext.LinksEnabled, ext.AttachmentsEnabled, ext.Comments.Enabled);
        }

        var target = await _importTargetFactory.CreateAsync(endpointOptions, ct).ConfigureAwait(false);
        var checkpointingService = _checkpointingFactory.Create(context.StateStore);

        // Resolve the strategy at execution time — the factory creates the correct implementation
        // based on the module config and target connection parameters.
        var resolutionStrategy = await _resolutionStrategyFactory
            .CreateAsync(ext.ResolutionStrategy, target, endpointOptions, ct)
            .ConfigureAwait(false);

        // Derive the SQLite idmap.db from the package URI (legacy fallback handled by factory)
        var idMapStore = _idMapStoreFactory.CreateFromPackageUri(job.Package.PackageUri);

        // NodeEnsurer: pre-create missing classification nodes before the revision import loop.
        if (_nodeEnsurer == null)
            _logger.LogWarning("[WorkItems] NodeEnsurer is not available — AutoCreateNodes will be skipped. Register INodeCreator (via a connector-specific implementation) to enable import-side node creation.");

        if (_nodeEnsurer != null)
        {
            var sourceProjectNameForEnsurer = job.Source?.GetProject() ?? string.Empty;
            var ensurerContext = new DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping(sourceProjectNameForEnsurer, project);
            await _nodeEnsurer.EnsureReferencedPathsAsync(ensurerContext, endpointOptions, context.ArtefactStore, ct, _metrics, job.JobId).ConfigureAwait(false);
        }

        // Build processor — use NodeTranslation-aware overload when available.
        IRevisionFolderProcessor processor;
        var sourceProjectName = job.Source?.GetProject() ?? string.Empty;
        var nodeStructureContext = new DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping(sourceProjectName, project);

        processor = _processorFactory.Create(
            target, idMapStore, checkpointingService, _identityLookupTool, context.ArtefactStore,
            nodeStructureContext);

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
            filterOptions: importFilters.Count > 0 ? importFilters : null,
            metrics: _metrics,
            jobId: job.JobId);

        var resumeMode = job.Resume?.Mode ?? ResumeMode.Auto;
        await orchestrator.ImportAsync(ext, resumeMode, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Import complete.");
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
