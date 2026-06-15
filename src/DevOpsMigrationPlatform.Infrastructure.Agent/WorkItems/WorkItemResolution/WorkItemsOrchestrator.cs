// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

/// <summary>
/// Orchestrates WorkItems capture, export, prepare, import, and validate phases through one symmetric contract.
///
/// Memory guarantee (import): processes one revision folder at a time; no in-memory list accumulation.
/// </summary>
public sealed class WorkItemsOrchestrator : IWorkItemsOrchestrator
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly ActivitySource s_discoveryActivitySource = new(WellKnownActivitySourceNames.Discovery);
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Export/capture/prepare deps
    private readonly IWorkItemRevisionSourceFactory _sourceFactory;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly IWorkItemFetchService? _fetchService;
    private readonly IWorkItemExportOrchestratorFactory _exportOrchestratorFactory;
    private readonly IWorkItemDiscoveryService? _discoveryService;
    private readonly IExportProgressStoreFactory? _exportProgressStoreFactory;
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly IInventoryOrchestrator? _inventoryOrchestrator;
    private readonly IRepoDiscoveryService? _repoDiscoveryService;
    private readonly ImportPreparer _importPreparer;
    private readonly AttachmentsWorkItemExtension _attachmentsExtension;
    private readonly CommentsWorkItemExtension _commentsExtension;

    // Shared deps
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly ILogger<WorkItemsModule> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly IOptions<WorkItemsModuleOptions> _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;

    // Import deps (absorbed from WorkItemsImportRuntime)
    private readonly IWorkItemTargetFactory _importTargetFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IWorkItemResolutionProcessorFactory _processorFactory;
    private readonly IIdentityTranslationTool? _identityTranslationTool;
    private readonly IWorkItemsImportCapabilityValidator _capabilityValidator;
    private readonly IWorkItemsNodeReadinessOrchestrator _nodeReadinessOrchestrator;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly IOptions<WorkItemOptions>? _workItemOptions;
    private readonly IOptions<NodesModuleOptions>? _nodesModuleOptions;

    public WorkItemsOrchestrator(
        IWorkItemRevisionSourceFactory sourceFactory,
        IAttachmentBinarySource? attachmentBinarySource,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory,
        IWorkItemFetchService? fetchService,
        IWorkItemExportOrchestratorFactory exportOrchestratorFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        ILogger<WorkItemsModule> logger,
        IPlatformMetrics? metrics,
        IWorkItemDiscoveryService? discoveryService,
        IExportProgressStoreFactory? exportProgressStoreFactory,
        IReferencedPathTracker? referencedPathTracker,
        IOptions<WorkItemsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ImportPreparer importPreparer,
        IWorkItemTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IWorkItemResolutionProcessorFactory processorFactory,
        IIdentityTranslationTool? identityTranslationTool,
        IWorkItemsImportCapabilityValidator capabilityValidator,
        IWorkItemsNodeReadinessOrchestrator nodeReadinessOrchestrator,
        ITargetEndpointInfo targetEndpointInfo,
        IOptions<WorkItemOptions>? workItemOptions = null,
        IOptions<NodesModuleOptions>? nodesModuleOptions = null,
        IInventoryOrchestrator? inventoryOrchestrator = null,
        IRepoDiscoveryService? repoDiscoveryService = null,
        AttachmentsWorkItemExtension? attachmentsExtension = null,
        CommentsWorkItemExtension? commentsExtension = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _fetchService = fetchService;
        _exportOrchestratorFactory = exportOrchestratorFactory ?? throw new ArgumentNullException(nameof(exportOrchestratorFactory));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _discoveryService = discoveryService;
        _exportProgressStoreFactory = exportProgressStoreFactory;
        _referencedPathTracker = referencedPathTracker;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _importPreparer = importPreparer ?? throw new ArgumentNullException(nameof(importPreparer));
        _importTargetFactory = importTargetFactory ?? throw new ArgumentNullException(nameof(importTargetFactory));
        _resolutionStrategyFactory = resolutionStrategyFactory ?? throw new ArgumentNullException(nameof(resolutionStrategyFactory));
        _idMapStoreFactory = idMapStoreFactory ?? throw new ArgumentNullException(nameof(idMapStoreFactory));
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _identityTranslationTool = identityTranslationTool;
        _capabilityValidator = capabilityValidator ?? throw new ArgumentNullException(nameof(capabilityValidator));
        _nodeReadinessOrchestrator = nodeReadinessOrchestrator ?? throw new ArgumentNullException(nameof(nodeReadinessOrchestrator));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _workItemOptions = workItemOptions;
        _nodesModuleOptions = nodesModuleOptions;
        _inventoryOrchestrator = inventoryOrchestrator;
        _repoDiscoveryService = repoDiscoveryService;
        _attachmentsExtension = attachmentsExtension ?? new AttachmentsWorkItemExtension(Options.Create(new AttachmentsExtensionOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.AttachmentReplayTool>.Instance);
        _commentsExtension = commentsExtension ?? new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()));
    }

    /// <summary>
    /// Inventory/capture phase — enumerates work items via <see cref="IWorkItemDiscoveryService"/>,
    /// streams progress through <see cref="IInventoryOrchestrator"/> (or writes a per-project
    /// inventory file directly when no inventory orchestrator is present), and records metrics.
    /// </summary>
    public async Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var project = context.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            _logger.LogError("[WorkItems] CaptureAsync called with empty Project — executor contract violated. Skipping.");
            return TaskExecutionResult.Skipped("CaptureAsync called with empty project.");
        }

        var endpoint = context.SourceEndpoint;
        if (endpoint is null)
        {
            _logger.LogError("[WorkItems] CaptureAsync called with null SourceEndpoint — executor contract violated. Skipping.");
            return TaskExecutionResult.Skipped("CaptureAsync called with null source endpoint.");
        }

        using var activity = s_discoveryActivitySource.StartActivity("inventory.workitems");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", "WorkItems");
        activity?.SetTag("org", endpoint.ResolvedUrl ?? string.Empty);
        activity?.SetTag("project", project);

        _logger.LogInformation("Inventorying {Module} for {Project}", "WorkItems", project);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Inventorying",
            Message = "Inventorying WorkItems",
            Timestamp = DateTimeOffset.UtcNow
        });

        var totalWorkItems = 0;
        var totalRevisions = 0;

        if (_discoveryService is not null)
        {
            OrganisationEndpoint nonNullEndpoint = endpoint!;
            if (_inventoryOrchestrator is not null)
            {
                async IAsyncEnumerable<InventoryProgressEvent> BuildEventStream(
                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken streamCt = default)
                {
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
                                Module = "WorkItems",
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

                await _inventoryOrchestrator.RunAsync("WorkItems", BuildEventStream(), context, ct: ct).ConfigureAwait(false);
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
                            Module = "WorkItems",
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

                await ProjectInventoryFile.MergeAsync(
                    context.Package, orgSlug, project,
                    orgUrl: orgUrl,
                    workItems: projectWorkItems,
                    revisions: projectRevisions,
                    repos: reposForProject,
                    isComplete: true,
                    ct: ct).ConfigureAwait(false);
            }
        }

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", "WorkItems" } };
        _metrics?.RecordInventoryWorkItems(totalWorkItems, tags);
        var durationMs = sw.Elapsed.TotalMilliseconds;
        _metrics?.RecordInventoryWorkItemsDuration(durationMs, tags);
        if (totalWorkItems == 0)
        {
            _metrics?.RecordInventoryWorkItemsErrors(tags);
            _logger.LogWarning("Zero items inventoried for {Module} in {Project}", "WorkItems", project);
        }
        _logger.LogInformation("Inventoried {Module}: {Count} items in {DurationMs}ms", "WorkItems", totalWorkItems, durationMs);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Inventoried",
            Message = "WorkItems inventory complete",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Discovery = new DiscoveryCounters
                {
                    Inventory = new InventoryCounters { RevisionsTotal = totalRevisions }
                }
            }
        });

        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("workitems.export");

        var job = context.Job;

        var orgUrl = _sourceEndpointInfo.Url;
        var orgSlug = _sourceEndpointInfo.OrganisationSlug;
        var project = _sourceEndpointInfo.Project;

#if !NET481
        var ext = WorkItemsModuleExtensions.FromOptions(_options.Value);

        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation(
                "[WorkItems] Exporting from {OrgUrl}/{Project} (attachments={AttachmentsEnabled}, comments={CommentsEnabled})",
                orgUrl, project, _attachmentsExtension.IsEnabled, _commentsExtension.IsEnabled);
        }

        if (_attachmentsExtension.IsEnabled && _attachmentBinarySource == null)
            _logger.LogWarning("[WorkItems] AttachmentsEnabled is true but no IAttachmentBinarySource is registered — attachment binaries will NOT be written to the package. Register a connector-specific IAttachmentBinarySource to enable attachment export.");
        if (_commentsExtension.IsEnabled && _inlineCommentSourceFactory == null)
            _logger.LogWarning("[WorkItems] Comments.Enabled is true but no IWorkItemCommentSourceFactory is registered — inline comments will NOT be exported. Register a connector-specific IWorkItemCommentSourceFactory to enable comment export.");

        var allFilters = ext.IncludeFilters.Concat(ext.ExcludeFilters).ToList();

        if (allFilters.Count > 0 && _fetchService == null)
            _logger.LogWarning("[WorkItems] IncludeFilters/ExcludeFilters are configured but no IWorkItemFetchService is registered — filters will be ignored and all work items will be exported. Register a connector-specific IWorkItemFetchService to enable filtered export.");
#else
        _logger.LogInformation("[WorkItems] Exporting from {OrgUrl}/{Project} (attachments=true, comments=false)", orgUrl, project);
        var wiqlQuery = (string?)null;
        var discoveryService481 = _discoveryService;
        var allFilters = new List<WorkItemFieldFilterOptions>();
#endif

        var source = await _sourceFactory
            .CreateAsync(ct)
            .ConfigureAwait(false);

#if !NET481
        if (_referencedPathTracker is null)
            _logger.LogWarning("[WorkItems] IReferencedPathTracker is not available — referenced path tracking will be skipped.");

        if (_referencedPathTracker is not null)
            await _referencedPathTracker.InitializeAsync(context.Package, orgSlug, project, ct).ConfigureAwait(false);
#endif

        var checkpointingService = _checkpointingFactory.Create(context.Package);

#if !NET481
        var inlineFactory = _commentsExtension.IsEnabled ? _inlineCommentSourceFactory : null;
#else
        var inlineFactory = (IWorkItemCommentSourceFactory?)null;
#endif

        var orchestrator = _exportOrchestratorFactory.Create(
            context.Package,
            orgSlug,
            project,
            checkpointingService,
#if !NET481
            _attachmentsExtension.IsEnabled ? _attachmentBinarySource : null,
#else
            _attachmentBinarySource,
#endif
            context.ProgressSink,
            inlineCommentSourceFactory: inlineFactory,
            fetchService: allFilters.Count > 0 ? _fetchService : null,
            filterOptions: allFilters.Count > 0 ? allFilters : null,
            metrics: _metrics,
            jobId: job.JobId,
            logger: _logger,
            taskId: context.TaskId,
#if !NET481
            wiqlQuery: ext.Query,
            discoveryService: _discoveryService,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: null,
            referencedPathTracker: _referencedPathTracker
#else
            wiqlQuery: wiqlQuery,
            discoveryService: discoveryService481,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: null
#endif
            );

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("prepare.workitems");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", "WorkItems");

        _logger.LogInformation("Preparing {Module}", "WorkItems");
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Preparing",
            Message = "Preparing WorkItems",
            Timestamp = DateTimeOffset.UtcNow
        });

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", "WorkItems" } };
        PrepareReport report;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            report = await _importPreparer.PrepareAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics?.RecordPrepareWorkItemsError(tags);
            _logger.LogError(ex, "[WorkItems] Prepare phase dispatch failed.");
            throw new InvalidOperationException("[WorkItems] Prepare phase dispatch failed.", ex);
        }
        finally
        {
            stopwatch.Stop();
        }

        _metrics?.RecordPrepareWorkItemsResolved(report.ResolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsUnresolved(report.UnresolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsDuration(stopwatch.Elapsed.TotalMilliseconds, tags);
        var org = _sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(org))
            org = context.TargetEndpoint.OrganisationSlug;

        var project = _sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(project))
            project = context.TargetEndpoint.Project;

        if (string.IsNullOrWhiteSpace(org))
            org = "unknown";
        if (string.IsNullOrWhiteSpace(project))
            project = "unknown";

        await WritePackageTextAsync(
            context.Package,
            new PackageContentContext(PackageContentKind.Artefact, Organisation: org, Project: project, Module: "WorkItems", Address: new RelativePathAddress("prepare-report.json")),
            JsonSerializer.Serialize(report),
            ct).ConfigureAwait(false);
        if (report.ImportReadinessReport is not null)
        {
            using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report.ImportReadinessReport)), writable: false);
            await context.Package.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.WorkItemsImportReadiness),
                new PackageMetaPayload(stream),
                ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Prepared WorkItems: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms",
            report.ResolvedCount,
            report.UnresolvedCount,
            stopwatch.ElapsedMilliseconds);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Prepared",
            Message = "WorkItems prepare complete",
            Timestamp = DateTimeOffset.UtcNow
        });

        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("workitems.import");

        var job = context.Job;
        _capabilityValidator.Validate();

        var orgUrl = _targetEndpointInfo.Url;
        var project = _targetEndpointInfo.Project;
        var startupPolicy = AssembleStartupPolicy(job);
        var ext = startupPolicy.Extensions;
        context.ProgressSink.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "StartupPolicy",
            Message = "Assembled WorkItems startup policy.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation(
            "[WorkItems] Importing into {OrgUrl}/{Project} (revisions={Revisions}, links={Links}, attachments={Attachments}, comments={Comments})",
            orgUrl, project, _options.Value.Extensions.Revisions.Enabled, _options.Value.Extensions.Links.Enabled, _attachmentsExtension.IsEnabled, _commentsExtension.IsEnabled);

        if (!_options.Value.Extensions.Revisions.Enabled)
        {
            _logger.LogInformation("[WorkItems] Revisions disabled — import phase skipped.");
            return TaskExecutionResult.Skipped("Revisions disabled.");
        }

        var target = await _importTargetFactory.CreateAsync(ct).ConfigureAwait(false);
        var checkpointingService = _checkpointingFactory.Create(context.Package);

        var resolutionStrategy = await _resolutionStrategyFactory
            .CreateAsync(ext.ResolutionStrategy, target, _targetEndpointInfo, ct)
            .ConfigureAwait(false);

        var idMapConnection = await context.Package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, ct).ConfigureAwait(false);
        var idMapStore = _idMapStoreFactory.Create(idMapConnection);

        var sourceOrganisation = _sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(sourceOrganisation))
            sourceOrganisation = _targetEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(sourceOrganisation))
            sourceOrganisation = !string.IsNullOrWhiteSpace(_targetEndpointInfo.Url)
                ? PackagePathResolver.DeriveInventoryOrgSlug(_targetEndpointInfo.Url)
                : "unknown";

        var sourceProjectName = _sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(sourceProjectName))
            sourceProjectName = project;
        if (string.IsNullOrWhiteSpace(sourceProjectName))
            sourceProjectName = "unknown";

        var nodeReadinessContext = new ProjectMapping(sourceProjectName, project);
        var replicateSourceTree = _nodesModuleOptions?.Value.ReplicateSourceTree ?? false;
        await _nodeReadinessOrchestrator
            .EnsureReadyAsync(nodeReadinessContext, replicateSourceTree, context, sourceOrganisation, sourceProjectName, ct)
            .ConfigureAwait(false);
        context.ProgressSink.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "NodeReadiness",
            Message = "Completed WorkItems node readiness checks.",
            Timestamp = DateTimeOffset.UtcNow
        });

        var nodeStructureContext = new ProjectMapping(sourceProjectName, project);
        var (attachmentsEnabled, linksEnabled, embeddedImagesEnabled) = ComputeLeveredExtensionFlags();
        var processor = _processorFactory.Create(
            target,
            idMapStore,
            checkpointingService,
            _identityTranslationTool,
            sourceOrganisation,
            sourceProjectName,
            nodeStructureContext,
            attachmentsEnabledByLever: attachmentsEnabled,
            linksEnabledByLever: linksEnabled,
            embeddedImagesEnabledByLever: embeddedImagesEnabled);

        var importFilters = startupPolicy.ImportFilters;

        context.ProgressSink.Emit(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "RevisionDispatch",
            Message = "Dispatching revision import.",
            Timestamp = DateTimeOffset.UtcNow
        });

        var jobScope = new WorkItemRevisionJobScope(
            Package: context.Package,
            Organisation: sourceOrganisation,
            Project: sourceProjectName,
            Checkpointing: checkpointingService,
            ProgressSink: context.ProgressSink,
            ResolutionStrategy: resolutionStrategy,
            IdMapStore: idMapStore,
            Processor: processor,
            Target: target,
            JobId: job.JobId,
            FilterOptions: importFilters.Count > 0 ? importFilters : null);

        await RunRevisionFolderLoopAsync(jobScope, ext, startupPolicy.ResumeMode, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Import complete.");
        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var job = context.Job;

        if (job.Kind != JobKind.Import)
            return TaskExecutionResult.Skipped("WorkItems package validation applies only to import jobs.");

        var found = false;
        await foreach (var _ in context.Package.EnumerateContentAsync(
                           new PackageContentContext(PackageContentKind.Collection,
                               Organisation: _sourceEndpointInfo.OrganisationSlug,
                               Project: _sourceEndpointInfo.Project,
                               Module: "WorkItems",
                               IsCollectionRequest: true),
                           ct).ConfigureAwait(false))
        {
            found = true;
            break;
        }

        if (!found)
        {
            context.Errors.Add(new ValidationError
            {
                Path = "WorkItems/",
                Message = "The package contains no work item revision folders under WorkItems/. Ensure an export has been run before attempting import."
            });
        }

        return TaskExecutionResult.Completed();
    }

    // -------------------------------------------------------------------------
    // Import: revision folder loop
    // -------------------------------------------------------------------------

    internal async Task RunRevisionFolderLoopAsync(
        WorkItemRevisionJobScope scope,
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
    {
        using var rootActivity = s_activitySource.StartActivity("workitems.import.loop", ActivityKind.Internal);
        rootActivity?.SetTag("job.id", scope.JobId ?? "not-set");

        using var _dc = DataClassificationScope.Begin(DataClassification.Customer);

        var commentsEnabled = _commentsExtension.IsEnabled;
        var (loopAttachmentsEnabled, _, loopEmbeddedImagesEnabled) = ComputeLeveredExtensionFlags();

        if (resumeMode == ResumeMode.ForceFresh)
        {
            await scope.Checkpointing.DeleteCursorAsync("import.workitems", ct).ConfigureAwait(false);
            _logger.LogInformation("[WorkItems] Force-fresh: cursor deleted. idmap.db preserved.");
        }

        var cursor = await scope.Checkpointing.ReadCursorAsync("import.workitems", ct).ConfigureAwait(false);
        var lastProcessed = cursor?.LastProcessed ?? string.Empty;
        var lastStage = cursor?.Stage;

        _logger.LogInformation(
            "[WorkItems] Starting import. Resume cursor: {Cursor} at stage {Stage}",
            string.IsNullOrEmpty(lastProcessed) ? "(start)" : lastProcessed,
            lastStage ?? "(none)");

        await scope.Processor.InitializeAsync(scope.ResolutionStrategy, ct).ConfigureAwait(false);

        int foldersProcessed = 0;
        int workItemsProcessed = 0;
        int lastImportedWorkItemId = 0;
        int revisionsForCurrentWorkItem = 0;
        HashSet<int>? filteredWorkItemIds = null;

        var importTags = MetricsTagList.Create(scope.JobId ?? "not-set", "import", "workitems");
        var workItemStopwatch = Stopwatch.StartNew();
        Activity? workItemActivity = null;

        try
        {
            if (scope.FilterOptions is { Count: > 0 })
                filteredWorkItemIds = await BuildFilteredWorkItemIdSetAsync(scope, ct).ConfigureAwait(false);

            await foreach (var folderPath in EnumerateRevisionFoldersAsync(scope, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                var resumeDecision = ImportResumeDecisionResolver.Resolve(folderPath, cursor);
                if (resumeDecision.ShouldSkip)
                    continue;

                var resumeAtStage = resumeDecision.ResumeAtStage;

                var folderName = GetFolderName(folderPath);
                var segments = folderName.Split('-');

                if (IsCommentFolder(segments))
                {
                    if (commentsEnabled)
                        await ProcessCommentFolderAsync(scope, folderPath, segments, ct).ConfigureAwait(false);
                    else
                        await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
                }
                else if (_options.Value.Extensions.Revisions.Enabled)
                {
                    ParseRevisionFolder(folderName, out var wiId, out var revIdx);

                    if (filteredWorkItemIds is not null && !filteredWorkItemIds.Contains(wiId))
                    {
                        _logger.LogInformation("[WorkItems] Work item {WorkItemId} skipped by import filter scope.", wiId);
                        await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
                        foldersProcessed++;
                        continue;
                    }

                    var lastRevIdx = await scope.IdMapStore.GetLastRevisionIndexAsync(wiId, ct).ConfigureAwait(false);
                    if (lastRevIdx.HasValue && revIdx <= lastRevIdx.Value)
                    {
                        _logger.LogDebug(
                            "[WorkItems] WI {WorkItemId} rev {Rev} at or below watermark {Watermark} — skipped.",
                            wiId, revIdx, lastRevIdx.Value);
                        await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
                        foldersProcessed++;
                        continue;
                    }

                    if (wiId != lastImportedWorkItemId)
                    {
                        if (lastImportedWorkItemId != 0 && _metrics != null)
                        {
                            _metrics.RecordWorkItemCompleted(importTags);
                            _metrics.RecordWorkItemDuration(workItemStopwatch.Elapsed.TotalMilliseconds, importTags);
                            _metrics.RecordRevisionCount(revisionsForCurrentWorkItem, importTags);
                            _metrics.DecrementInFlight(importTags);
                        }
                        workItemActivity?.Dispose();

                        _metrics?.RecordWorkItemAttempted(importTags);
                        _metrics?.IncrementInFlight(importTags);
                        workItemStopwatch.Restart();
                        revisionsForCurrentWorkItem = 1;
                        lastImportedWorkItemId = wiId;
                        workItemsProcessed++;

                        workItemActivity = s_activitySource.StartActivity("workitem.import", ActivityKind.Internal);
                        workItemActivity?.SetTag("job.id", scope.JobId ?? "not-set");
                        workItemActivity?.SetTag("workitem.id", wiId);
                    }
                    else
                    {
                        revisionsForCurrentWorkItem++;
                    }

                    using var revisionActivity = s_activitySource.StartActivity("revision.process", ActivityKind.Internal);
                    revisionActivity?.SetTag("workitem.id", wiId);
                    revisionActivity?.SetTag("revision.index", revIdx);

                    EmitReplaySkipVisibilityEvents(scope, loopAttachmentsEnabled, loopEmbeddedImagesEnabled, resumeAtStage);

                    await scope.Processor.ProcessAsync(folderPath, resumeAtStage, scope.ResolutionStrategy, ct)
                        .ConfigureAwait(false);

                    await scope.IdMapStore.UpdateLastRevisionIndexAsync(wiId, revIdx, ct).ConfigureAwait(false);
                }
                else
                {
                    await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
                }

                var eventSegments = GetFolderName(folderPath).Split('-');
                int.TryParse(eventSegments.Length >= 2 ? eventSegments[1] : null, out _);

                foldersProcessed++;
                scope.ProgressSink.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = CursorStage.Completed,
                    Timestamp = DateTimeOffset.UtcNow,
                    LastCheckpointAt = DateTimeOffset.UtcNow,
                    NextCheckpointDueAt = null
                });
            }
        }
        finally
        {
            if (lastImportedWorkItemId != 0 && _metrics != null)
                _metrics.DecrementInFlight(importTags);
            workItemActivity?.Dispose();
        }

        _logger.LogInformation(
            "[WorkItems] Import complete. Folders processed: {Count}, work items: {WI}",
            foldersProcessed, workItemsProcessed);

        if (lastImportedWorkItemId != 0 && _metrics != null)
        {
            _metrics.RecordWorkItemCompleted(importTags);
            _metrics.RecordWorkItemDuration(workItemStopwatch.Elapsed.TotalMilliseconds, importTags);
            _metrics.RecordRevisionCount(revisionsForCurrentWorkItem, importTags);
        }

        if (scope.FilterOptions is { Count: > 0 } && workItemsProcessed == 0)
        {
            _logger.LogWarning(
                "[WorkItems] Warning: all work items were filtered out by filter scopes. Check your filter configuration.");
        }
    }

    // -------------------------------------------------------------------------
    // Import: filtering
    // -------------------------------------------------------------------------

    private async Task<HashSet<int>> BuildFilteredWorkItemIdSetAsync(WorkItemRevisionJobScope scope, CancellationToken ct)
    {
        var lastRevisionFolderByWorkItem = new Dictionary<int, string>();
        await foreach (var folderPath in EnumerateRevisionFoldersAsync(scope, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var folderName = GetFolderName(folderPath);
            var segments = folderName.Split('-');
            if (IsCommentFolder(segments))
                continue;

            ParseRevisionFolder(folderName, out var workItemId, out _);
            if (workItemId <= 0)
                continue;

            lastRevisionFolderByWorkItem[workItemId] = folderPath;
        }

        var includedIds = new HashSet<int>();
        foreach (var entry in lastRevisionFolderByWorkItem)
        {
            ct.ThrowIfCancellationRequested();
            var workItemId = entry.Key;
            var folderPath = entry.Value;
            if (await RevisionFolderPassesFilterAsync(scope, workItemId, folderPath, scope.FilterOptions!, ct).ConfigureAwait(false))
                includedIds.Add(workItemId);
        }

        return includedIds;
    }

    private async Task<bool> RevisionFolderPassesFilterAsync(
        WorkItemRevisionJobScope scope,
        int workItemId,
        string folderPath,
        IReadOnlyList<WorkItemFieldFilterOptions> filterOptions,
        CancellationToken ct)
    {
        var json = await ReadPackageTextAsync(scope, CombineFolderFile(folderPath, "revision.json"), ct).ConfigureAwait(false);
        if (json is null)
            return false;

        WorkItemRevision? revision = null;
        try
        {
            revision = JsonSerializer.Deserialize<WorkItemRevision>(json, s_jsonOptions);
        }
        catch
        {
            return false;
        }

        if (revision is null)
            return false;

        var fields = revision.Fields.ToDictionary(
            f => f.ReferenceName,
            f => (object?)f.Value);
        var fetchedItem = new FetchedWorkItem(workItemId, fields);

        try
        {
            return WorkItemFieldFilterEvaluator.PassesFilters(fetchedItem, filterOptions);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            _logger.LogWarning(
                "[WorkItems] Regex filter timeout evaluating work item {WorkItemId} — treating as non-match.",
                workItemId);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Import: comment folder handling
    // -------------------------------------------------------------------------

    private async Task ProcessCommentFolderAsync(
        WorkItemRevisionJobScope scope,
        string folderPath,
        string[] segments,
        CancellationToken ct)
    {
        if (!int.TryParse(segments[1], out var sourceWorkItemId))
        {
            _logger.LogWarning("[WorkItems] Cannot parse work item ID from comment folder {Folder} — skipping.", folderPath);
            await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
            return;
        }

        var targetId = await scope.IdMapStore.GetTargetWorkItemIdAsync(sourceWorkItemId, ct).ConfigureAwait(false);
        if (targetId is null)
        {
            _logger.LogWarning("[WorkItems] No target mapping for source {SourceId} — skipping comment folder {Folder}.", sourceWorkItemId, folderPath);
            await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
            return;
        }

        var commentJson = await ReadPackageTextAsync(scope, CombineFolderFile(folderPath, "comment.json"), ct).ConfigureAwait(false);
        if (commentJson is null)
        {
            _logger.LogWarning("[WorkItems] comment.json not found in {Folder} — skipping.", folderPath);
            await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
            return;
        }

        WorkItemComment? comment;
        try
        {
            comment = JsonSerializer.Deserialize<WorkItemComment>(commentJson, s_jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WorkItems] Failed to deserialise comment.json in {Folder} — skipping.", folderPath);
            await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
            return;
        }

        if (comment is not null && !comment.IsDeleted)
        {
            var text = comment.RenderedText ?? comment.Text;
            await scope.Target.CreateCommentAsync(targetId.Value, text, ct).ConfigureAwait(false);
        }

        await WriteCompletedCursorAsync(scope, folderPath, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Import: package enumeration
    // -------------------------------------------------------------------------

    private async IAsyncEnumerable<string> EnumerateRevisionFoldersAsync(
        WorkItemRevisionJobScope scope,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var scopedContext = new PackageContentContext(
            PackageContentKind.Collection,
            Organisation: scope.Organisation,
            Project: scope.Project,
            Module: "WorkItems",
            IsCollectionRequest: true);

        var yieldedAny = false;
        await foreach (var folderPath in EnumerateRevisionFoldersFromContextAsync(scope, scopedContext, ct).ConfigureAwait(false))
        {
            yieldedAny = true;
            yield return folderPath;
        }

        if (yieldedAny)
            yield break;

        await foreach (var folderPath in EnumerateRevisionFoldersFromAllContentAsync(scope, ct).ConfigureAwait(false))
            yield return folderPath;
    }

    private async IAsyncEnumerable<string> EnumerateRevisionFoldersFromContextAsync(
        WorkItemRevisionJobScope scope,
        PackageContentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var paths = scope.Package.EnumerateContentAsync(context, ct);
        if (paths is null)
            yield break;

        string? previousPath = null;
        await foreach (var path in paths.ConfigureAwait(false))
        {
            var candidateFolderPath = TryGetRevisionFolderPath(path);
            if (candidateFolderPath is null || candidateFolderPath.Length == 0)
                continue;
            var folderPath = candidateFolderPath;

            if (previousPath is not null && string.CompareOrdinal(folderPath, previousPath) < 0)
            {
                throw new InvalidOperationException(
                    $"WorkItems package enumeration must be lexicographic ascending. Previous='{previousPath}', Current='{folderPath}'.");
            }

            if (string.Equals(folderPath, previousPath, StringComparison.Ordinal))
                continue;

            previousPath = folderPath;
            yield return folderPath;
        }
    }

    private async IAsyncEnumerable<string> EnumerateRevisionFoldersFromAllContentAsync(
        WorkItemRevisionJobScope scope,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var allPaths = scope.Package.EnumerateAllAsync(ct);
        if (allPaths is null)
            yield break;

        string? previousPath = null;
        await foreach (var path in allPaths.ConfigureAwait(false))
        {
            var candidateFolderPath = TryGetRevisionFolderPath(path);
            if (candidateFolderPath is null || candidateFolderPath.Length == 0)
                continue;

            var folderPath = candidateFolderPath;
            if (previousPath is not null && string.CompareOrdinal(folderPath, previousPath) < 0)
            {
                throw new InvalidOperationException(
                    $"WorkItems package enumeration must be lexicographic ascending. Previous='{previousPath}', Current='{folderPath}'.");
            }

            if (string.Equals(folderPath, previousPath, StringComparison.Ordinal))
                continue;

            previousPath = folderPath;
            yield return folderPath;
        }
    }

    // -------------------------------------------------------------------------
    // Import: visibility events and cursor
    // -------------------------------------------------------------------------

    private void EmitReplaySkipVisibilityEvents(WorkItemRevisionJobScope scope, bool attachmentsEnabled, bool embeddedImagesEnabled, string? resumeAtStage)
    {
        if (!embeddedImagesEnabled && WorkItemRevisionStagePipeline.ShouldRunStage(CursorStage.AppliedFields, resumeAtStage))
        {
            scope.ProgressSink.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = CursorStage.AppliedFields,
                Message = "Embedded image replay skipped because the replay lever is disabled.",
                Timestamp = DateTimeOffset.UtcNow,
                LastCheckpointAt = DateTimeOffset.UtcNow,
                NextCheckpointDueAt = null
            });
        }

        if (!attachmentsEnabled && WorkItemRevisionStagePipeline.ShouldRunStage(CursorStage.UploadedAttachments, resumeAtStage))
        {
            scope.ProgressSink.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = CursorStage.UploadedAttachments,
                Message = "Attachment replay skipped because the replay lever is disabled.",
                Timestamp = DateTimeOffset.UtcNow,
                LastCheckpointAt = DateTimeOffset.UtcNow,
                NextCheckpointDueAt = null
            });
        }
    }

    private static Task WriteCompletedCursorAsync(WorkItemRevisionJobScope scope, string folderPath, CancellationToken ct)
        => scope.Checkpointing.WriteCursorAsync("import.workitems", new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

    // -------------------------------------------------------------------------
    // Import: startup policy
    // -------------------------------------------------------------------------

    private ImportStartupPolicy AssembleStartupPolicy(Job job)
    {
        var extensions = WorkItemsModuleExtensions.FromOptions(_options.Value);
        var importFilters = extensions.IncludeFilters.Concat(extensions.ExcludeFilters).ToList();
        var resumeMode = job.Resume?.Mode ?? ResumeMode.Auto;
        return new ImportStartupPolicy(extensions, importFilters, resumeMode);
    }

    private (bool attachments, bool links, bool embeddedImages) ComputeLeveredExtensionFlags()
    {
        var moduleExt = _options.Value.Extensions;
        if (_workItemOptions is null)
            return (moduleExt.Attachments.Enabled, moduleExt.Links.Enabled, moduleExt.EmbeddedImages.Enabled);

        var replayOptions = _workItemOptions.Value;
        var hasExplicitLeverConfig =
            replayOptions.RevisionReplay ||
            replayOptions.LinkReplay ||
            replayOptions.AttachmentReplay ||
            replayOptions.EmbeddedImageReplay ||
            replayOptions.FieldTransform;

        if (!hasExplicitLeverConfig)
            return (moduleExt.Attachments.Enabled, moduleExt.Links.Enabled, moduleExt.EmbeddedImages.Enabled);

        return (
            moduleExt.Attachments.Enabled && (!replayOptions.RevisionReplay || replayOptions.AttachmentReplay),
            moduleExt.Links.Enabled && (!replayOptions.RevisionReplay || replayOptions.LinkReplay),
            moduleExt.EmbeddedImages.Enabled && (!replayOptions.RevisionReplay || replayOptions.EmbeddedImageReplay)
        );
    }



    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static async Task WritePackageTextAsync(IPackageAccess package, PackageContentContext context, string content, CancellationToken cancellationToken)
    {
        using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await package.PersistContentAsync(context, new PackagePayload(stream, "application/json"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ReadPackageTextAsync(WorkItemRevisionJobScope scope, string path, CancellationToken ct)
    {
        var payload = await scope.Package.RequestContentAsync(
            CreateArtefactContext(scope, path),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static PackageContentContext CreateArtefactContext(WorkItemRevisionJobScope scope, string path)
    {
        if (path.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: scope.Organisation,
                Project: scope.Project,
                Module: "WorkItems",
                Address: new WorkItemRevisionAddress(GetRevisionFolderFromPath(path)));
        }

        return new PackageContentContext(
            PackageContentKind.Artefact,
            Organisation: scope.Organisation,
            Project: scope.Project,
            Module: "WorkItems",
            Address: new WorkItemAttachmentAddress(GetRevisionFolderFromPath(path), GetFileName(path)));
    }

    private static string? TryGetRevisionFolderPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var hadTrailingSlash = normalized.EndsWith("/", StringComparison.Ordinal);
        var folderCandidate = normalized.TrimEnd('/');
        if (LooksLikeRevisionFolderPath(folderCandidate))
            return hadTrailingSlash ? $"{folderCandidate}/" : folderCandidate;

        if (!folderCandidate.EndsWith("/revision.json", StringComparison.OrdinalIgnoreCase)
            && !folderCandidate.EndsWith("/comment.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lastSlash = folderCandidate.LastIndexOf('/');
        if (lastSlash <= 0)
            return null;
        return folderCandidate.Substring(0, lastSlash);
    }

    private static bool LooksLikeRevisionFolderPath(string normalizedFolderPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedFolderPath))
            return false;

        var folderName = GetFolderName(normalizedFolderPath);
        var segments = folderName.Split('-');
        if (segments.Length < 3)
            return false;

        if (!int.TryParse(segments[1], out _))
            return false;

        return int.TryParse(segments[2], out _)
               || segments[2].StartsWith("c", StringComparison.OrdinalIgnoreCase);
    }

    private static void ParseRevisionFolder(string folderName, out int workItemId, out int revisionIndex)
    {
        var segments = folderName.Split('-');
        int.TryParse(segments.Length >= 2 ? segments[1] : null, out workItemId);
        int.TryParse(segments.Length >= 3 ? segments[2] : null, out revisionIndex);
    }

    private static bool IsCommentFolder(string[] segments)
        => segments.Length >= 3 && segments[2].StartsWith("c", StringComparison.OrdinalIgnoreCase);

    private static string CombineFolderFile(string folderPath, string fileName)
        => $"{folderPath.TrimEnd('/')}/{fileName}";

    private static string GetRevisionFolderFromPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var wiIdx = normalized.LastIndexOf("WorkItems/", StringComparison.OrdinalIgnoreCase);
        if (wiIdx >= 0)
            normalized = normalized.Substring(wiIdx + "WorkItems/".Length);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized.Substring(0, lastSlash) : normalized;
    }

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
    }

    private static string GetFolderName(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
    }

    // -------------------------------------------------------------------------
    // Private types
    // -------------------------------------------------------------------------

    private sealed record ImportStartupPolicy(
        WorkItemsModuleExtensions Extensions,
        System.Collections.Generic.List<WorkItemFieldFilterOptions> ImportFilters,
        ResumeMode ResumeMode);
}
