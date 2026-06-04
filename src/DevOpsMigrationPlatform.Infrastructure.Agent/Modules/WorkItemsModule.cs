// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
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
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Nodes;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemType;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

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
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Import),
        new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Import),
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
    private readonly IWorkItemTargetFactory _importTargetFactory;
    private readonly IWorkItemExportOrchestratorFactory _exportOrchestratorFactory;
    private readonly IWorkItemResolutionStrategyFactory _resolutionStrategyFactory;
    private readonly IIdMapStoreFactory _idMapStoreFactory;
    private readonly IWorkItemResolutionProcessorFactory _processorFactory;
    private readonly IIdentityTranslationTool? _identityTranslationTool;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly ILogger<WorkItemsModule> _logger;
    private readonly ILogger<WorkItemsImportRuntime> _orchestratorLogger;
    private readonly IWorkItemsOrchestrator _workItemsOrchestrator;
    private readonly IWorkItemsOrchestratorFactory _workItemsOrchestratorFactory;
    private readonly IWorkItemFetchService? _fetchService;
    private readonly IInventoryOrchestrator? _inventoryOrchestrator;
    private readonly IPlatformMetrics? _metrics;

    private readonly IWorkItemDiscoveryService? _discoveryService;
    private readonly IExportProgressStoreFactory? _exportProgressStoreFactory;
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly INodesOrchestrator? _nodesOrchestrator;
    private readonly NodeReadinessOrchestrator? _nodeReadinessOrchestrator;
    private readonly IOptions<NodesModuleOptions>? _nodesModuleOptions;
    private readonly IOptions<WorkItemsModuleOptions> _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IRepoDiscoveryService? _repoDiscoveryService;
    private readonly ImportPreparer _importPreparer;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly IIdentityMappingService? _identityMappingService;
    private readonly INodeTranslationTool? _nodeTranslationTool;
    private readonly IFieldTransformTool _fieldTransformTool;
    private readonly IOptions<WorkItemOptions>? _workItemImportOptions;

    public WorkItemsModule(
        IWorkItemRevisionSourceFactory sourceFactory,
        ILogger<WorkItemsModule> logger,
        IOptions<WorkItemsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ILogger<WorkItemsImportRuntime> orchestratorLogger,
        IWorkItemTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IWorkItemResolutionProcessorFactory processorFactory,
        ITargetEndpointInfo targetEndpointInfo,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        IWorkItemFetchService? fetchService = null,
        IInventoryOrchestrator? inventoryOrchestrator = null,
        IPlatformMetrics? metrics = null,
        IPlatformMetrics? PlatformMetrics = null,
        IWorkItemDiscoveryService? discoveryService = null,
        IExportProgressStoreFactory? exportProgressStoreFactory = null,
        IReferencedPathTracker? referencedPathTracker = null,
        INodesOrchestrator? nodesOrchestrator = null,
        NodeReadinessOrchestrator? nodeReadinessOrchestrator = null,
        IOptions<NodesModuleOptions>? nodesModuleOptions = null,
        IIdentityMappingService? identityMappingService = null,
        INodeTranslationTool? nodeTranslationTool = null,
        IFieldTransformTool? fieldTransformTool = null,
        IOptions<WorkItemOptions>? workItemImportOptions = null,
        IWorkItemExportOrchestratorFactory? exportOrchestratorFactory = null,
        IWorkItemsOrchestratorFactory? workItemsOrchestratorFactory = null,
        IWorkItemsOrchestrator? workItemsOrchestrator = null,
        IIdentityTranslationTool? identityTranslationTool = null,
        IRepoDiscoveryService? repoDiscoveryService = null,
        IEnumerable<IImportFailurePattern>? importFailurePatterns = null,
        ImportPreparer? importPreparer = null)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _orchestratorLogger = orchestratorLogger ?? throw new ArgumentNullException(nameof(orchestratorLogger));
        _exportOrchestratorFactory = exportOrchestratorFactory ?? new WorkItemExportOrchestratorFactory();
        _importTargetFactory = importTargetFactory ?? throw new ArgumentNullException(nameof(importTargetFactory));
        _resolutionStrategyFactory = resolutionStrategyFactory ?? throw new ArgumentNullException(nameof(resolutionStrategyFactory));
        _idMapStoreFactory = idMapStoreFactory ?? throw new ArgumentNullException(nameof(idMapStoreFactory));
        _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _attachmentBinarySource = attachmentBinarySource;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _fetchService = fetchService;
        _inventoryOrchestrator = inventoryOrchestrator;
        _metrics = metrics ?? PlatformMetrics;
        _discoveryService = discoveryService;
        _exportProgressStoreFactory = exportProgressStoreFactory;
        _referencedPathTracker = referencedPathTracker;
        _nodesOrchestrator = nodesOrchestrator;
        _nodeReadinessOrchestrator = nodeReadinessOrchestrator;
        _nodesModuleOptions = nodesModuleOptions;
        _identityMappingService = identityMappingService ?? throw new ArgumentNullException(nameof(identityMappingService));
        _nodeTranslationTool = nodeTranslationTool ?? throw new ArgumentNullException(nameof(nodeTranslationTool));
        _fieldTransformTool = fieldTransformTool ?? throw new ArgumentNullException(nameof(fieldTransformTool));
        _workItemImportOptions = workItemImportOptions;
        _identityTranslationTool = identityTranslationTool;
        _workItemsOrchestratorFactory = workItemsOrchestratorFactory ?? new WorkItemsOrchestratorFactory();
        _repoDiscoveryService = repoDiscoveryService;
        var resolvedFailurePatterns = importFailurePatterns?.ToArray();
        resolvedFailurePatterns = resolvedFailurePatterns is { Length: > 0 }
            ? resolvedFailurePatterns
            : CreateDefaultImportFailurePatterns().ToArray();
        _importPreparer = importPreparer ?? new ImportPreparer(_options, _sourceEndpointInfo.OrganisationSlug, _sourceEndpointInfo.Project, resolvedFailurePatterns);
        _workItemsOrchestrator = workItemsOrchestrator
            ?? _workItemsOrchestratorFactory.Create(
                _importTargetFactory,
                _resolutionStrategyFactory,
                _checkpointingFactory,
                _idMapStoreFactory,
                _processorFactory,
                _identityTranslationTool,
                new WorkItemsImportCapabilityValidator(_fieldTransformTool),
                new WorkItemsNodeReadinessOrchestrator(_nodeReadinessOrchestrator, _nodesOrchestrator, _metrics, _logger),
                _metrics,
                _orchestratorLogger,
                _logger,
                _sourceEndpointInfo,
                _targetEndpointInfo,
                _options,
                _workItemImportOptions,
                _nodesModuleOptions,
                _sourceFactory,
                _attachmentBinarySource,
                _inlineCommentSourceFactory,
                _fetchService,
                _exportOrchestratorFactory,
                _discoveryService,
                _exportProgressStoreFactory,
                _referencedPathTracker,
                _importPreparer);
    }

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

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } };
        _metrics?.RecordInventoryWorkItems(totalWorkItems, tags);
        var durationMs = sw.Elapsed.TotalMilliseconds;
        _metrics?.RecordInventoryWorkItemsDuration(durationMs, tags);
        if (totalWorkItems == 0)
        {
            _metrics?.RecordInventoryWorkItemsErrors(tags);
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

        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        return await _workItemsOrchestrator.PrepareAsync(context, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<IImportFailurePattern> CreateDefaultImportFailurePatterns()
    {
        return
        [
            new MissingRevisionArtefactImportFailurePattern(),
            new InvalidRevisionPayloadImportFailurePattern(),
            new MissingAttachmentBinaryImportFailurePattern(),
            new MissingEmbeddedImageBinaryImportFailurePattern(),
            new FieldTransformCompatibilityImportFailurePattern()
        ];
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
    {
        return await _workItemsOrchestrator.ExportAsync(context, ct).ConfigureAwait(false);
    }

    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        return await _workItemsOrchestrator.ImportAsync(context, ct).ConfigureAwait(false);
    }

#if !NET481
    private WorkItemsModuleExtensions ApplyImportReplayLevers(WorkItemsModuleExtensions ext)
    {
        if (_workItemImportOptions is null)
            return ext;

        var replayOptions = _workItemImportOptions.Value;
        var hasExplicitLeverConfig =
            replayOptions.RevisionReplay ||
            replayOptions.LinkReplay ||
            replayOptions.AttachmentReplay ||
            replayOptions.EmbeddedImageReplay ||
            replayOptions.FieldTransform;

        // Preserve current defaults when WorkItem options are not explicitly configured.
        if (!hasExplicitLeverConfig)
            return ext;

        var attachmentsEnabled = ext.AttachmentsEnabled &&
                                 (!replayOptions.RevisionReplay || replayOptions.AttachmentReplay);
        var linksEnabled = ext.LinksEnabled &&
                           (!replayOptions.RevisionReplay || replayOptions.LinkReplay);
        var embeddedImagesEnabled = ext.EmbeddedImages.Enabled &&
                                    (!replayOptions.RevisionReplay || replayOptions.EmbeddedImageReplay);

        return new WorkItemsModuleExtensions
        {
            Query = ext.Query,
            RevisionsEnabled = ext.RevisionsEnabled,
            LinksEnabled = linksEnabled,
            AttachmentsEnabled = attachmentsEnabled,
            Comments = ext.Comments,
            EmbeddedImages = new EmbeddedImagesExtensionOptionsConfig
            {
                Enabled = embeddedImagesEnabled,
                DownloadTimeoutSeconds = ext.EmbeddedImages.DownloadTimeoutSeconds
            },
            ResolutionStrategy = ext.ResolutionStrategy,
            IncludeFilters = ext.IncludeFilters,
            ExcludeFilters = ext.ExcludeFilters
        };
    }
#endif


    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        return await _workItemsOrchestrator.ValidateAsync(context, ct).ConfigureAwait(false);
    }

}
