// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
#endif
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Import),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Import)
    };
    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
    public bool SupportsImport => true;
    public bool SupportsValidate => false;

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly ActivitySource s_discoveryActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
#if !NET481
    private readonly IWorkItemImportTargetFactory _importTargetFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IRevisionFolderProcessorFactory _processorFactory;
#endif
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly ILogger<WorkItemsModule> _logger;
#if !NET481
    private readonly ILogger<WorkItemImportOrchestrator> _orchestratorLogger;
#endif
    private readonly IWorkItemFetchService? _fetchService;
    private readonly IInventoryOrchestrator? _inventoryOrchestrator;
    private readonly IPlatformMetrics? _metrics;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly IWorkItemDiscoveryService? _discoveryService;
    private readonly IExportProgressStoreFactory? _exportProgressStoreFactory;
#if !NET481
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly INodesOrchestrator? _nodesOrchestrator;
#endif
    private readonly IOptions<WorkItemsModuleOptions> _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IRepoDiscoveryService? _repoDiscoveryService;
#if !NET481
    private readonly ITargetEndpointInfo _targetEndpointInfo;
#endif

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger,
        IOptions<WorkItemsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
#if !NET481
        ILogger<WorkItemImportOrchestrator> orchestratorLogger,
        IWorkItemImportTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IRevisionFolderProcessorFactory processorFactory,
        ITargetEndpointInfo targetEndpointInfo,
#else
        ICheckpointingServiceFactory checkpointingFactory,
#endif
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        IWorkItemFetchService? fetchService = null,
        IInventoryOrchestrator? inventoryOrchestrator = null,
        IPlatformMetrics? metrics = null,
        IPlatformMetrics? PlatformMetrics = null,
        IWorkItemDiscoveryService? discoveryService = null,
        IExportProgressStoreFactory? exportProgressStoreFactory = null,
#if !NET481
        IReferencedPathTracker? referencedPathTracker = null,
        INodesOrchestrator? nodesOrchestrator = null,
#endif
        IIdentityLookupTool? identityLookupTool = null,
        IRepoDiscoveryService? repoDiscoveryService = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
#if !NET481
        _orchestratorLogger = orchestratorLogger ?? throw new ArgumentNullException(nameof(orchestratorLogger));
        _importTargetFactory = importTargetFactory ?? throw new ArgumentNullException(nameof(importTargetFactory));
        _resolutionStrategyFactory = resolutionStrategyFactory ?? throw new ArgumentNullException(nameof(resolutionStrategyFactory));
        _idMapStoreFactory = idMapStoreFactory ?? throw new ArgumentNullException(nameof(idMapStoreFactory));
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
#endif
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _fetchService = fetchService;
        _inventoryOrchestrator = inventoryOrchestrator;
        _metrics = metrics;
        _PlatformMetrics = PlatformMetrics;
        _discoveryService = discoveryService;
        _exportProgressStoreFactory = exportProgressStoreFactory;
#if !NET481
        _referencedPathTracker = referencedPathTracker;
        _nodesOrchestrator = nodesOrchestrator;
#endif
        _identityLookupTool = identityLookupTool;
        _repoDiscoveryService = repoDiscoveryService;
    }

    public async Task CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var project = context.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            _logger.LogError("[WorkItems] CaptureAsync called with empty Project — executor contract violated. Skipping.");
            return;
        }

        var endpoint = context.SourceEndpoint;
        if (endpoint is null)
        {
            _logger.LogError("[WorkItems] CaptureAsync called with null SourceEndpoint — executor contract violated. Skipping.");
            return;
        }

        using var activity = s_discoveryActivitySource.StartActivity("inventory.workitems");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        activity?.SetTag("org", endpoint.ResolvedUrl ?? string.Empty);
        activity?.SetTag("project", project);

        _logger.LogInformation("Inventorying {Module} for {Project}", Name, project);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Inventorying",
            Message = $"Inventorying {Name}",
            Timestamp = DateTimeOffset.UtcNow
        });

        var totalWorkItems = 0;
        var totalRevisions = 0;

        if (_discoveryService is not null)
        {
            OrganisationEndpoint nonNullEndpoint = endpoint!;
            if (_inventoryOrchestrator is not null)
            {
                // Stream every window event directly — both intermediate (IsComplete=false) heartbeats
                // and the final (IsComplete=true) completion event. This provides live progress
                // updates visible in the CLI/TUI as work items are discovered.
                async IAsyncEnumerable<InventoryProgressEvent> BuildEventStream(
                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken streamCt = default)
                {
                    // Count repos before the first yield so we can populate ReposCount on
                    // the final (IsComplete=true) event. Awaiting before the first yield in an
                    // async iterator is valid C#.
                    var reposForProject = 0;
                    if (_repoDiscoveryService is not null)
                    {
                        try
                        {
                            reposForProject = await _repoDiscoveryService
                                .CountReposAsync(nonNullEndpoint, project, streamCt)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to count repos for project {Project}; defaulting to 0.", project);
                        }
                    }

                    await foreach (var summary in _discoveryService
                        .DiscoverWorkItemsAsync(nonNullEndpoint, project,
                            progress: new Progress<int>(n => context.ProgressSink?.Emit(new ProgressEvent
                            {
                                Module = Name,
                                Stage = "Inventorying",
                                Message = $"[WorkItems] Discovered {n:N0} work items…",
                                Timestamp = DateTimeOffset.UtcNow
                            })),
                            cancellationToken: streamCt)
                        .ConfigureAwait(false))
                    {
                        if (summary.IsWorkItemComplete)
                        {
                            totalWorkItems += summary.WorkItemsCount;
                            totalRevisions += summary.RevisionsCount;
                        }

                        yield return new InventoryProgressEvent
                        {
                            Url = nonNullEndpoint.ResolvedUrl!,
                            ProjectName = project,
                            WorkItemsCount = summary.WorkItemsCount,
                            RevisionsCount = summary.RevisionsCount,
                            ReposCount = summary.IsWorkItemComplete ? reposForProject : 0,
                            IsComplete = summary.IsWorkItemComplete,
                            Error = summary.Error,
                        };
                    }
                }

                await _inventoryOrchestrator.RunAsync(Name, BuildEventStream(), context, ct: ct).ConfigureAwait(false);
            }
            else
            {
                var orgUrl = nonNullEndpoint.ResolvedUrl!;
                var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(orgUrl);

                var reposForProject = 0;
                if (_repoDiscoveryService is not null)
                {
                    try
                    {
                        reposForProject = await _repoDiscoveryService
                            .CountReposAsync(nonNullEndpoint, project, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to count repos for project {Project}; defaulting to 0.", project);
                    }
                }

                long projectWorkItems = 0;
                long projectRevisions = 0;
                await foreach (var summary in _discoveryService
                    .DiscoverWorkItemsAsync(nonNullEndpoint, project,
                        progress: new Progress<int>(n => context.ProgressSink?.Emit(new ProgressEvent
                        {
                            Module = Name,
                            Stage = "Inventorying",
                            Message = $"[WorkItems] Discovered {n:N0} work items…",
                            Timestamp = DateTimeOffset.UtcNow
                        })),
                        cancellationToken: ct)
                    .ConfigureAwait(false))
                {
                    if (summary.IsWorkItemComplete)
                    {
                        projectWorkItems += summary.WorkItemsCount;
                        projectRevisions += summary.RevisionsCount;
                        totalWorkItems += summary.WorkItemsCount;
                        totalRevisions += summary.RevisionsCount;
                    }
                }

                var projectPath = PackagePathResolver.ProjectInventoryPath(orgSlug, project);
                await ProjectInventoryFile.MergeAsync(
                    context.ArtefactStore, projectPath,
                    orgUrl: orgUrl, project: project,
                    workItems: projectWorkItems,
                    revisions: projectRevisions,
                    repos: reposForProject,
                    isComplete: true,
                    ct: ct).ConfigureAwait(false);
            }
        }

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } };
        _PlatformMetrics?.RecordInventoryWorkItems(totalWorkItems, tags);
        var durationMs = sw.Elapsed.TotalMilliseconds;
        _PlatformMetrics?.RecordInventoryWorkItemsDuration(durationMs, tags);
        if (totalWorkItems == 0)
        {
            _PlatformMetrics?.RecordInventoryWorkItemsErrors(tags);
            _logger.LogWarning("Zero items inventoried for {Module} in {Project}", Name, project);
        }
        _logger.LogInformation("Inventoried {Module}: {Count} items in {DurationMs}ms", Name, totalWorkItems, durationMs);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Inventoried",
            Message = $"{Name} inventory complete",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Discovery = new DiscoveryCounters
                {
                    Inventory = new InventoryCounters { RevisionsTotal = totalRevisions }
                }
            }
        });
    }

    public async Task PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("prepare.workitems");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);

        _logger.LogInformation("Preparing {Module}", Name);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Preparing",
            Message = $"Preparing {Name}",
            Timestamp = DateTimeOffset.UtcNow
        });

        var report = new PrepareReport
        {
            ModuleName = Name,
            ResolvedCount = 0
        };

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } };
        _metrics?.RecordPrepareWorkItemsResolved(report.ResolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsUnresolved(report.UnresolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsDuration(0, tags);
        await context.ArtefactStore.WriteAsync("WorkItems/prepare-report.json", JsonSerializer.Serialize(report), ct).ConfigureAwait(false);

        _logger.LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", Name, report.ResolvedCount, report.UnresolvedCount, 0);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Prepared",
            Message = $"{Name} prepare complete",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("workitems.export");

        var job = context.Job;

        var orgUrl = _sourceEndpointInfo.Url;
        var project = _sourceEndpointInfo.Project;

#if !NET481
        var ext = WorkItemsModuleExtensions.FromOptions(_options.Value);

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
#else
        // net481: use defaults — attachments enabled, no filters, no comments.
        _logger.LogInformation("[WorkItems] Exporting from {OrgUrl}/{Project} (attachments=true, comments=false)", orgUrl, project);
        var wiqlQuery = (string?)null;
        var discoveryService481 = _discoveryService;
        var allFilters = new System.Collections.Generic.List<WorkItemFieldFilterOptions>();
#endif

        // NOTE: Connectors now resolve their own credentials from DI; no need to pass endpoint options.
        var source = await _sourceFactory
            .CreateAsync(ct)
            .ConfigureAwait(false);

#if !NET481
        if (_referencedPathTracker is null)
            _logger.LogWarning("[WorkItems] IReferencedPathTracker is not available — referenced path tracking will be skipped.");

        if (_referencedPathTracker is not null)
            await _referencedPathTracker.InitializeAsync(context.ArtefactStore, ct).ConfigureAwait(false);
#endif

        var checkpointingService = _checkpointingFactory.Create(context.StateStore);

        // Comments extension gates inline comment fetching.
#if !NET481
        var inlineFactory = ext.Comments.Enabled ? _inlineCommentSourceFactory : null;
#else
        var inlineFactory = (IWorkItemCommentSourceFactory?)null;
#endif

        var orchestrator = new WorkItemExportOrchestrator(
            context.ArtefactStore,
            checkpointingService,
#if !NET481
            ext.AttachmentsEnabled ? _attachmentBinarySource : null,
#else
            _attachmentBinarySource,
#endif
            context.ProgressSink,
            endpoint: null, // Connectors now resolve from DI
            project: project,
            inlineCommentSourceFactory: inlineFactory,
            fetchService: allFilters.Count > 0 ? _fetchService : null,
            filterOptions: allFilters.Count > 0 ? allFilters : null,
            metrics: _metrics,
            jobId: job.JobId,
            logger: _logger,
#if !NET481
            wiqlQuery: ext.Query,
            discoveryService: _discoveryService,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: job.Package.PackageUri,
            referencedPathTracker: _referencedPathTracker
#else
            wiqlQuery: wiqlQuery,
            discoveryService: discoveryService481,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: job.Package.PackageUri
#endif
            );

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
    }

    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        // TFS is a source-only connector — import is not supported.
        _logger.LogDebug("[WorkItems] Import not supported on net481 (TFS agent) — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
#else

        var job = context.Job;

        var orgUrl = _targetEndpointInfo.Url;
        var project = _targetEndpointInfo.Project;

        var ext = WorkItemsModuleExtensions.FromOptions(_options.Value);

        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation(
                "[WorkItems] Importing into {OrgUrl}/{Project} (revisions={Revisions}, links={Links}, attachments={Attachments}, comments={Comments})",
                orgUrl, project, ext.RevisionsEnabled, ext.LinksEnabled, ext.AttachmentsEnabled, ext.Comments.Enabled);
        }

        // NOTE: Connectors now resolve their own credentials from DI; no need to pass endpoint options.
        var target = await _importTargetFactory.CreateAsync(ct).ConfigureAwait(false);
        var checkpointingService = _checkpointingFactory.Create(context.StateStore);

        // Resolve the strategy at execution time — the factory creates the correct implementation
        // based on the module config and target connection parameters.
        // TODO T051+: IWorkItemResolutionStrategyFactory and ITeamTarget still require MigrationEndpointOptions
        // These interfaces need IOptions<TargetEndpointOptions> injection or to be split into Info + Options
        var resolutionStrategy = await _resolutionStrategyFactory
            .CreateAsync(ext.ResolutionStrategy, target, null!, ct)
            .ConfigureAwait(false);

        // Derive the SQLite idmap.db from the package URI (legacy fallback handled by factory)
        var idMapStore = _idMapStoreFactory.CreateFromPackageUri(job.Package.PackageUri);

        // NodesOrchestrator: pre-create missing classification nodes before the revision import loop.
        if (_nodesOrchestrator == null)
            _logger.LogWarning("[WorkItems] NodesOrchestrator is not available — AutoCreateNodes will be skipped. Register INodesOrchestrator to enable import-side node creation.");
        else
        {
            var sourceProjectName = _sourceEndpointInfo.Project;
            var ensurerContext = new DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping(sourceProjectName, project);
            await _nodesOrchestrator.EnsureReferencedPathsAsync(ensurerContext, context.ArtefactStore, ct, _metrics, job.JobId).ConfigureAwait(false);
        }

        // Build processor — use NodeTranslation-aware overload when available.
        IRevisionFolderProcessor processor;
        var sourceProjectNameForProcessor = _sourceEndpointInfo.Project;
        var nodeStructureContext = new DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping(sourceProjectNameForProcessor, project);

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
#endif
    }


    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var job = context.Job;

        // Only perform package-side validation for Import
        if (job.Kind != JobKind.Import)
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
