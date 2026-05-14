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
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
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
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
#endif
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
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
    private readonly NodeReadinessOrchestrator? _nodeReadinessOrchestrator;
    private readonly IOptions<NodesModuleOptions>? _nodesModuleOptions;
#endif
    private readonly IPackageAccess? _package;
    private readonly IOptions<WorkItemsModuleOptions> _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IRepoDiscoveryService? _repoDiscoveryService;
    private readonly IReadOnlyList<IImportFailurePattern> _importFailurePatterns;
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
        NodeReadinessOrchestrator? nodeReadinessOrchestrator = null,
        IOptions<NodesModuleOptions>? nodesModuleOptions = null,
#endif
        IIdentityLookupTool? identityLookupTool = null,
        IRepoDiscoveryService? repoDiscoveryService = null,
        IEnumerable<IImportFailurePattern>? importFailurePatterns = null,
        IPackageAccess? package = null)
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
        _nodeReadinessOrchestrator = nodeReadinessOrchestrator;
        _nodesModuleOptions = nodesModuleOptions;
#endif
        _identityLookupTool = identityLookupTool;
        _repoDiscoveryService = repoDiscoveryService;
        var resolvedFailurePatterns = importFailurePatterns?.ToArray() ?? Array.Empty<IImportFailurePattern>();
        _importFailurePatterns = resolvedFailurePatterns.Length == 0
            ? CreateDefaultImportFailurePatterns()
            : resolvedFailurePatterns;
        _package = package;
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

        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
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

        var stopwatch = Stopwatch.StartNew();
        var report = await BuildPrepareReportAsync(context, ct).ConfigureAwait(false);
        stopwatch.Stop();

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } };
        _metrics?.RecordPrepareWorkItemsResolved(report.ResolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsUnresolved(report.UnresolvedCount, tags);
        _metrics?.RecordPrepareWorkItemsDuration(stopwatch.Elapsed.TotalMilliseconds, tags);
        await context.ArtefactStore.WriteAsync("WorkItems/prepare-report.json", JsonSerializer.Serialize(report), ct).ConfigureAwait(false);
        if (report.ImportReadinessReport is not null)
        {
            await context.ArtefactStore
                .WriteAsync(".mission/Readiness/workitems-import-readiness.json", JsonSerializer.Serialize(report.ImportReadinessReport), ct)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms",
            Name,
            report.ResolvedCount,
            report.UnresolvedCount,
            stopwatch.ElapsedMilliseconds);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Prepared",
            Message = $"{Name} prepare complete",
            Timestamp = DateTimeOffset.UtcNow
        });

        return TaskExecutionResult.Completed();
    }

    private async Task<PrepareReport> BuildPrepareReportAsync(PrepareContext context, CancellationToken ct)
    {
        var importFailurePatternContext = new ImportFailurePatternContext(context, _options.Value);
        var failureFindings = new List<ImportFailureFinding>();
        foreach (var pattern in _importFailurePatterns)
        {
            var findings = await pattern.EvaluateAsync(importFailurePatternContext, ct).ConfigureAwait(false);
            if (findings.Count > 0)
            {
                failureFindings.AddRange(findings);
            }
        }

        var readiness = failureFindings.Any(f => f.Severity == ImportFailureSeverity.Blocking)
            ? WorkItemsPrepareReadinessResult.ChangesRequired
            : WorkItemsPrepareReadinessResult.Ready;

        var unresolvedItems = failureFindings
            .Select(f => new UnresolvedItem(
                f.EvidenceKey,
                $"{f.PatternCode}: {f.Message}",
                f.Severity == ImportFailureSeverity.Blocking ? PrepareIssueSeverity.Blocking : PrepareIssueSeverity.Warning))
            .ToList();
        var artefactFindings = MapArtefactFindings(failureFindings);
        var fieldTransformFindings = MapFieldTransformFindings(failureFindings);
        var importReadinessReport = ImportReadinessReport.Create(
            readiness,
            failureFindings,
            artefactFindings,
            fieldTransformFindings);

        var resolvedCount = await CountRevisionArtefactsAsync(context, ct).ConfigureAwait(false);

        return new PrepareReport
        {
            ModuleName = Name,
            ResolvedCount = resolvedCount,
            UnresolvedItems = unresolvedItems,
            ArtefactFindings = artefactFindings,
            FieldTransformFindings = fieldTransformFindings,
            Readiness = readiness,
            ImportReadinessReport = importReadinessReport,
            FailureFindings = failureFindings
        };
    }

    private static IReadOnlyList<ArtefactFinding> MapArtefactFindings(IReadOnlyList<ImportFailureFinding> failureFindings)
    {
        var findings = new List<ArtefactFinding>();
        foreach (var failureFinding in failureFindings)
        {
            if (failureFinding.PatternCode == MissingRevisionArtefactImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.RevisionFolder,
                    "WorkItems",
                    ArtefactFindingStatus.Missing,
                    failureFinding.EvidenceKey));
            }
            else if (failureFinding.PatternCode == InvalidRevisionPayloadImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.RevisionFolder,
                    failureFinding.EvidenceKey,
                    ArtefactFindingStatus.Invalid,
                    failureFinding.EvidenceKey));
            }
            else if (failureFinding.PatternCode == MissingAttachmentBinaryImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.Attachment,
                    failureFinding.EvidenceKey,
                    ArtefactFindingStatus.Missing,
                    failureFinding.EvidenceKey));
            }
            else if (failureFinding.PatternCode == MissingEmbeddedImageBinaryImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.EmbeddedImage,
                    failureFinding.EvidenceKey,
                    ArtefactFindingStatus.Missing,
                    failureFinding.EvidenceKey));
            }
        }

        return findings;
    }

    private static IReadOnlyList<FieldTransformFinding> MapFieldTransformFindings(IReadOnlyList<ImportFailureFinding> failureFindings)
    {
        var findings = new List<FieldTransformFinding>();
        foreach (var failureFinding in failureFindings.Where(f => f.PatternCode == FieldTransformCompatibilityImportFailurePattern.Code))
        {
            var segments = failureFinding.EvidenceKey.Split('|');
            if (segments.Length >= 4
                && Enum.TryParse(segments[0], ignoreCase: true, out FieldTransformFindingStatus status))
            {
                findings.Add(new FieldTransformFinding(
                    segments[2],
                    segments[3],
                    segments[1],
                    status,
                    failureFinding.SuggestedAction));
                continue;
            }

            findings.Add(new FieldTransformFinding(
                failureFinding.EvidenceKey,
                "Unknown",
                failureFinding.PatternCode,
                FieldTransformFindingStatus.Error,
                failureFinding.SuggestedAction));
        }

        return findings;
    }

    private static async Task<int> CountRevisionArtefactsAsync(PrepareContext context, CancellationToken ct)
    {
        var resolvedCount = 0;
        await foreach (var artefactPath in context.ArtefactStore.EnumerateAsync("WorkItems/", ct).ConfigureAwait(false))
        {
            if (artefactPath.EndsWith("/revision.json", StringComparison.Ordinal))
            {
                resolvedCount++;
            }
        }

        return resolvedCount;
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
            taskId: context.TaskId,
#if !NET481
            wiqlQuery: ext.Query,
            discoveryService: _discoveryService,
            exportProgressStoreFactory: _exportProgressStoreFactory,
             packageUri: job.Package.PackageUri,
            package: _package,
            referencedPathTracker: _referencedPathTracker
#else
            wiqlQuery: wiqlQuery,
            discoveryService: discoveryService481,
            exportProgressStoreFactory: _exportProgressStoreFactory,
            packageUri: job.Package.PackageUri,
            package: _package
#endif
            );

        await orchestrator.ExportAsync(source, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Export complete.");
        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        // TFS is a source-only connector — import is not supported.
        _logger.LogDebug("[WorkItems] Import not supported on net481 (TFS agent) — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
        return TaskExecutionResult.Skipped("WorkItems import is not supported on net481.");
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

        var package = _package ?? throw new InvalidOperationException("IPackageAccess is required for native database access.");
        var idMapConnection = await package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, ct).ConfigureAwait(false);
        var idMapStore = _idMapStoreFactory.Create(idMapConnection);

        var sourceProjectName = _sourceEndpointInfo.Project;
        var nodeReadinessContext = new DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping(sourceProjectName, project);
        var replicateSourceTree = _nodesModuleOptions?.Value.ReplicateSourceTree ?? false;
        if (_nodeReadinessOrchestrator is not null)
        {
            await _nodeReadinessOrchestrator
                .ExecuteAsync(nodeReadinessContext, replicateSourceTree, ct)
                .ConfigureAwait(false);
        }
        else if (_nodesOrchestrator is not null)
        {
            _logger.LogWarning("[WorkItems] NodeReadinessOrchestrator is not available — falling back to INodesOrchestrator.EnsureReferencedPathsAsync.");
            await _nodesOrchestrator
                .EnsureReferencedPathsAsync(nodeReadinessContext, context.ArtefactStore, ct, _metrics, job.JobId)
                .ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning("[WorkItems] No node readiness orchestrator is available — node readiness dispatch will be skipped.");
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
            jobId: job.JobId,
            package: _package);

        var resumeMode = job.Resume?.Mode ?? ResumeMode.Auto;
        await orchestrator.ImportAsync(ext, resumeMode, ct).ConfigureAwait(false);

        _logger.LogInformation("[WorkItems] Import complete.");
        return TaskExecutionResult.Completed();
#endif
    }


    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var job = context.Job;

        // Only perform package-side validation for Import
        if (job.Kind != JobKind.Import)
            return TaskExecutionResult.Skipped("WorkItems package validation applies only to import jobs.");

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

        return TaskExecutionResult.Completed();
    }

}
