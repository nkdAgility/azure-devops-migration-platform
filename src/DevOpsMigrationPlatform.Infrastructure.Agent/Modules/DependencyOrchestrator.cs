// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates dependency discovery: checkpointing, progress events, metrics, CSV writing,
/// enumeration loops, and resume logic. Delegates actual link analysis to
/// <see cref="IDependencyDiscoveryService"/>.
/// </summary>
internal sealed class DependencyOrchestrator : IDependencyOrchestrator
{
    private const string ModuleName = "Dependencies";

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IPackageAccess? _package;

    public DependencyOrchestrator(
        ILogger<DependencyOrchestrator> logger,
        ICheckpointingServiceFactory checkpointingFactory,
        IPlatformMetrics? metrics = null,
        IPackageAccess? package = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _metrics = metrics;
        _package = package;
    }

    /// <summary>
    /// Normalizes an organisation URL + project name pair into a canonical key
    /// by trimming trailing slashes and lower-casing both parts.
    /// </summary>
    internal static string NormalizeProjectKey(string orgUrl, string project)
        => $"{orgUrl.TrimEnd('/').ToLowerInvariant()}|{project.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Performs dependency discovery during the Export phase. Writes dependencies.csv.
    /// </summary>
    public async Task AnalyseAsync(
        IDependencyDiscoveryService dependencyService,
        OrganisationsAnalyseContext context,
        JobPolicies policies,
        int checkpointIntervalSeconds,
        CancellationToken ct)
    {
        var organisations = context.Organisations.ToList();

        using var rootActivity = ActivitySource.StartActivity("discovery.dependencies", ActivityKind.Internal);
        rootActivity?.SetTag("job.id", context.Job.JobId);

        var job = context.Job;
        var sink = context.ProgressSink ?? NullProgressSink.Instance;
        IJobMetricsStore? metricsStore = null;
        IJobSnapshotStore? snapshotStore = null;

        _logger.LogInformation("Dependencies module starting for job {JobId}.", job.JobId);

        // ── Pre-count: load inventory.json for grand totals ──────────────────
        long grandTotalWorkItems = 0;
        InventoryReport? inventoryReport = null;
        var inventoryJson = await ReadIndexTextAsync(new PackageIndexContext("inventory.json"), ct).ConfigureAwait(false);
        if (inventoryJson is not null)
        {
            try
            {
                inventoryReport = JsonSerializer.Deserialize<InventoryReport>(inventoryJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                if (inventoryReport is not null)
                {
                    grandTotalWorkItems = inventoryReport.Totals.WorkItems;
                    _logger.LogInformation(
                        "Loaded inventory.json — {TotalWorkItems} total work items across {Projects} projects.",
                        inventoryReport.Totals.WorkItems, inventoryReport.Totals.Projects);

                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = "InventoryLoaded",
                        Message = $"Inventory loaded: {inventoryReport.Totals.WorkItems:N0} work items across {inventoryReport.Totals.Projects} projects.",
                        Timestamp = DateTimeOffset.UtcNow,
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters
                            {
                                OrganisationsTotal = inventoryReport.Organisations.Count,
                                ProjectsTotal = inventoryReport.Totals.Projects,
                                WorkItemsTotal = inventoryReport.Totals.WorkItems
                            },
                            Discovery = new DiscoveryCounters
                            {
                                Inventory = new InventoryCounters
                                {
                                    RevisionsTotal = inventoryReport.Totals.Revisions,
                                    RepositoriesTotal = inventoryReport.Totals.Repos
                                }
                            }
                        }
                    });

                    // Emit per-project seed events so TUI/CLI can pre-populate
                    // their project tracking dictionaries with inventory data.
                    foreach (var org in inventoryReport.Organisations)
                    {
                        foreach (var proj in org.Projects)
                        {
                            sink.Emit(new ProgressEvent
                            {
                                Module = ModuleName,
                                Stage = "InventorySeed",
                                Message = $"{org.Url}|{proj.Name}",
                                Timestamp = DateTimeOffset.UtcNow,
                                Metrics = new JobMetrics
                                {
                                    Scope = new JobScopeCounters
                                    {
                                        WorkItemsTotal = proj.WorkItems
                                    },
                                    Discovery = new DiscoveryCounters
                                    {
                                        Inventory = new InventoryCounters
                                        {
                                            RevisionsTotal = proj.Revisions,
                                            RepositoriesTotal = proj.Repos
                                        }
                                    }
                                }
                            });
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse inventory.json — proceeding without pre-counts.");
            }
        }
        else
        {
            _logger.LogInformation("No inventory.json found — per-project counts will be discovered during analysis.");
        }

        // ── Resume: read existing cursor and CSV ─────────────────────────────
        var completedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resumedProjectStats = new Dictionary<string, PerProjectStats>(StringComparer.OrdinalIgnoreCase);
        var existingCsvRows = new StringBuilder();
        var recordCount = 0;
        string? inProgressProjectKey = null;
        BatchContinuationToken? inProgressToken = null;
        int inProgressProcessedWorkItems = 0;
        int inProgressLinksFound = 0;
        int inProgressCrossProjectCount = 0;
        int inProgressCrossOrgCount = 0;

        // Aggregate dependency analysis is a fan-in step, not an authoritative resume owner.
        // The clean long-term model keeps checkpoint state at the per-project capture boundary.

        // Build the CSV — either from existing data or fresh header
        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine(
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemChangedDate,SourceWorkItemStateCategory");

        var checkpointInterval = TimeSpan.FromSeconds(checkpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;

        var metrics = _metrics;
        string? currentOrg = null;
        var jobSw = Stopwatch.StartNew();
        var orgSw = new Stopwatch();
        var projectSw = new Stopwatch();
        int orgProjectCount = 0;

        // Track completed projects as we go (union of pre-existing + newly completed)
        var allCompletedProjects = new HashSet<string>(completedProjects, StringComparer.OrdinalIgnoreCase);

        // Per-project stats for the cursor (seed with any stats from a previous run)
        var allProjectStats = new Dictionary<string, PerProjectStats>(resumedProjectStats, StringComparer.OrdinalIgnoreCase);

        // Per-project CSV builders keyed by "{orgFolder}/{project}" relative path.
        var perProjectCsv = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        // Track the current project's relative folder path so DependencyFoundEvent can append.
        string? currentProjectFolder = null;
        string? currentOrgFolder = null;
        // Per-org accumulator: all raw CSV lines for projects in the current org (for org-level CSV).
        var currentOrgCsvBuilder = new StringBuilder();
        var orgCsvHeaderWritten = false;

        const string CsvHeader =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemChangedDate,SourceWorkItemStateCategory";
        string? currentInProgressKey = inProgressProjectKey;
        BatchContinuationToken? currentInProgressToken = inProgressToken;
        int currentInProgressProcessed = inProgressProcessedWorkItems;
        int currentInProgressLinks = inProgressLinksFound;
        int currentInProgressCrossProject = inProgressCrossProjectCount;
        int currentInProgressCrossOrg = inProgressCrossOrgCount;

        // The aggregate fan-in step does not own continuation state.
        Task OnBatchCheckpoint(BatchContinuationToken token, CancellationToken checkpointCt)
        {
            currentInProgressToken = token;
            return Task.CompletedTask;
        }

        // All data within the processing loop references org URLs, project names, and WI IDs — customer data.
        using var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);

        await foreach (var evt in dependencyService.DiscoverDependenciesAsync(
            completedProjects, null, inProgressProjectKey, inProgressToken, OnBatchCheckpoint, ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case DependencyFoundEvent found:
                    var r = found.Record;
                    var csvLine =
                        $"{r.SourceWorkItemId},{EscapeCsv(r.SourceWorkItemType ?? "")}," +
                        $"{EscapeCsv(r.SourceProject ?? "")},{EscapeCsv(r.SourceOrganisationUrl)}," +
                        $"{EscapeCsv(r.LinkType ?? "")},{r.LinkScope}," +
                        $"{r.TargetWorkItemId},{EscapeCsv(r.TargetProject ?? "")}," +
                        $"{EscapeCsv(r.TargetOrganisation ?? "")},{r.TargetStatus}," +
                        $"{(r.LinkChangedDate.HasValue ? r.LinkChangedDate.Value.ToString("O") : "")}," +
                        $"{(r.SourceWorkItemChangedDate.HasValue ? r.SourceWorkItemChangedDate.Value.ToString("O") : "")}," +
                        $"{EscapeCsv(r.SourceWorkItemStateCategory ?? "")}";
                    csvBuilder.AppendLine(csvLine);
                    recordCount++;

                    // Also append to per-project CSV builder.
                    var recOrgFolder = PackagePathResolver.ExtractOrgFolderName(r.SourceOrganisationUrl);
                    var recProjectFolder = $"{recOrgFolder}/{PackagePathResolver.Sanitise(r.SourceProject ?? "unknown")}";
                    if (!perProjectCsv.TryGetValue(recProjectFolder, out var projCsv))
                    {
                        projCsv = new StringBuilder();
                        projCsv.AppendLine(CsvHeader);
                        perProjectCsv[recProjectFolder] = projCsv;
                    }
                    projCsv.AppendLine(csvLine);

                    // Also append to per-org CSV accumulator.
                    // Initialise or transition the org accumulator on the first event for a new org.
                    if (currentOrgFolder != recOrgFolder)
                    {
                        // New org seen — flush the previous org's CSV if any.
                        if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
                        {
                            await WriteOrgDependenciesAsync(currentOrgFolder, currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);
                            _logger.LogDebug("Flushed org-level dependencies CSV for {Org} (on org transition from DependencyFoundEvent).", currentOrgFolder);
                        }
                        currentOrgFolder = recOrgFolder;
                        currentOrgCsvBuilder = new StringBuilder();
                        orgCsvHeaderWritten = false;
                    }
                    if (!orgCsvHeaderWritten)
                    {
                        currentOrgCsvBuilder.AppendLine(CsvHeader);
                        orgCsvHeaderWritten = true;
                    }
                    currentOrgCsvBuilder.AppendLine(csvLine);

                    metrics?.RecordLinksFound(1, new MetricsTagList
                    {
                        { "job.id", job.JobId },
                        { "module", ModuleName },
                        { "organisation.url", r.SourceOrganisationUrl },
                        { "link.scope", r.LinkScope.ToString() }
                    });
                    break;

                case DependencyHeartbeatEvent heartbeat:
                    // When resuming an in-progress project, the ADO service reports counts
                    // from the resume point only. Offset with the saved counters so the
                    // progress display shows cumulative totals.
                    var hbProjectKey = NormalizeProjectKey(heartbeat.OrganisationUrl, heartbeat.ProjectName);
                    var isResumedProject = inProgressProjectKey is not null
                        && string.Equals(hbProjectKey, inProgressProjectKey, StringComparison.OrdinalIgnoreCase);
                    var adjustedAnalysed = heartbeat.WorkItemsAnalysed + (isResumedProject ? inProgressProcessedWorkItems : 0);
                    var adjustedLinks = heartbeat.ExternalLinksFound + (isResumedProject ? inProgressLinksFound : 0);
                    var adjustedCrossProj = heartbeat.CrossProjectCount + (isResumedProject ? inProgressCrossProjectCount : 0);
                    var adjustedCrossOrg = heartbeat.CrossOrgCount + (isResumedProject ? inProgressCrossOrgCount : 0);

                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = heartbeat.Error is not null
                            ? "Failed"
                            : heartbeat.IsComplete
                                ? "ProjectComplete"
                                : heartbeat.IsCounting
                                    ? "Counting"
                                    : "Analysis",
                        Message = $"{heartbeat.OrganisationUrl}|{heartbeat.ProjectName}",
                        Timestamp = DateTimeOffset.UtcNow,
                        LastCheckpointAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero),
                        NextCheckpointDueAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero) + checkpointInterval,
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = heartbeat.TotalWorkItems },
                            Discovery = new DiscoveryCounters
                            {
                                Dependencies = new DependencyCounters
                                {
                                    WorkItemsAnalysed = adjustedAnalysed,
                                    ExternalLinksFound = adjustedLinks,
                                    CrossProjectLinks = adjustedCrossProj,
                                    CrossOrgLinks = adjustedCrossOrg
                                }
                            }
                        }
                    });

                    // Flush CSV at checkpoint interval even mid-project so long-running
                    // projects produce visible output within minutes.
                    if (!heartbeat.IsComplete && heartbeat.Error is null)
                    {
                        // Track this project as in-progress for cursor checkpoints (use adjusted cumulative values)
                        currentInProgressKey = NormalizeProjectKey(heartbeat.OrganisationUrl, heartbeat.ProjectName);
                        currentInProgressProcessed = adjustedAnalysed;
                        currentInProgressLinks = adjustedLinks;
                        currentInProgressCrossProject = adjustedCrossProj;
                        currentInProgressCrossOrg = adjustedCrossOrg;
                        // Start timing the project on the first non-complete heartbeat.
                        if (!projectSw.IsRunning)
                            projectSw.Restart();

                        if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
                        {
                            await WriteRootDependenciesAsync(csvBuilder.ToString(), ct).ConfigureAwait(false);

                            // Also flush the current project's partial CSV.
                            var midOrgFolder = PackagePathResolver.ExtractOrgFolderName(heartbeat.OrganisationUrl);
                            var midProject = PackagePathResolver.Sanitise(heartbeat.ProjectName);
                            var midProjectFolder = $"{midOrgFolder}/{midProject}";
                            if (perProjectCsv.TryGetValue(midProjectFolder, out var midProjCsv))
                                await WriteProjectDependenciesAsync(midOrgFolder, midProject, midProjCsv.ToString(), ct).ConfigureAwait(false);

                            // Also flush the current org's partial CSV.
                            if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
                                await WriteOrgDependenciesAsync(currentOrgFolder, currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);

                            lastCheckpoint = DateTime.UtcNow;
                            _logger.LogDebug("Dependencies mid-project flush at checkpoint interval.");
                        }
                    }

                    if (heartbeat.IsComplete || heartbeat.Error is not null)
                    {
                        projectSw.Stop();
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogInformation(
                                "Completed project {Project} in {OrgUrl}: {Links} external links.",
                                heartbeat.ProjectName, heartbeat.OrganisationUrl, heartbeat.ExternalLinksFound);

                        var hbOrgFolder = PackagePathResolver.ExtractOrgFolderName(heartbeat.OrganisationUrl);
                        var hbProject = PackagePathResolver.Sanitise(heartbeat.ProjectName);
                        var hbProjectFolder = $"{hbOrgFolder}/{hbProject}";

                        // Organisation transition tracking (metrics only — CSV accumulator
                        // transitions are handled by the DependencyFoundEvent handler).
                        if (currentOrg != heartbeat.OrganisationUrl)
                        {
                            if (currentOrg is not null)
                            {
                                orgSw.Stop();
                                var orgCompleteTags = new MetricsTagList
                                {
                                    { "job.id", job.JobId },
                                    { "module", ModuleName },
                                    { "organisation.url", currentOrg }
                                };
                                metrics?.SetProjectCount(orgProjectCount, orgCompleteTags);
                                metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, orgCompleteTags);
                                metrics?.OrganisationCompleted(orgCompleteTags);
                            }
                            currentOrg = heartbeat.OrganisationUrl;
                            orgProjectCount = 0;
                            orgSw.Restart();
                            metrics?.OrganisationStarted(new MetricsTagList
                            {
                                { "job.id", job.JobId },
                                { "module", ModuleName },
                                { "organisation.url", heartbeat.OrganisationUrl }
                            });
                        }

                        // Update current project folder for DependencyFoundEvent tracking.
                        currentProjectFolder = hbProjectFolder;
                        if (currentOrgFolder is null)
                            currentOrgFolder = hbOrgFolder;

                        var projectTags = new MetricsTagList
                        {
                            { "job.id", job.JobId },
                            { "module", ModuleName },
                            { "organisation.url", heartbeat.OrganisationUrl },
                            { "project.name", heartbeat.ProjectName }
                        };
                        metrics?.ProjectStarted(projectTags);
                        if (heartbeat.Error is not null)
                        {
                            metrics?.ProjectFailed(projectTags);
                        }
                        else
                        {
                            metrics?.ProjectCompleted(projectTags);
                        }
                        metrics?.RecordProjectDuration(projectSw.Elapsed.TotalMilliseconds, projectTags);
                        projectSw.Reset();
                        metrics?.RecordWorkItemsAnalysed(heartbeat.WorkItemsAnalysed, projectTags);
                        orgProjectCount++;

                        // Track this project as completed for the cursor
                        var completedKey = NormalizeProjectKey(heartbeat.OrganisationUrl, heartbeat.ProjectName);
                        allCompletedProjects.Add(completedKey);

                        // Clear in-progress state — the project is now fully completed
                        if (string.Equals(currentInProgressKey, completedKey, StringComparison.OrdinalIgnoreCase))
                        {
                            currentInProgressKey = null;
                            currentInProgressToken = null;
                            currentInProgressProcessed = 0;
                            currentInProgressLinks = 0;
                            currentInProgressCrossProject = 0;
                            currentInProgressCrossOrg = 0;
                        }

                        // Save per-project stats so they are available when a resumed job
                        // emits synthetic ProjectComplete events for the CLI live table.
                        allProjectStats[completedKey] = new PerProjectStats(
                            WorkItemsAnalysed: adjustedAnalysed,
                            ExternalLinksFound: adjustedLinks,
                            CrossProjectCount: adjustedCrossProj,
                            CrossOrgCount: adjustedCrossOrg,
                            TotalWorkItems: heartbeat.TotalWorkItems);

                        // Flush per-project CSV to artefact store.
                        if (perProjectCsv.TryGetValue(hbProjectFolder, out var completedProjCsv))
                        {
                            await WriteProjectDependenciesAsync(hbOrgFolder, hbProject, completedProjCsv.ToString(), ct).ConfigureAwait(false);
                            _logger.LogDebug("Flushed per-project dependencies CSV for {Project}.", hbProjectFolder);
                        }

                        // Flush org-level CSV at every project boundary so it stays current.
                        if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
                        {
                            await WriteOrgDependenciesAsync(currentOrgFolder, currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);
                            _logger.LogDebug("Flushed org-level dependencies CSV for {Org} at project boundary.", currentOrgFolder);
                        }

                        // Flush root CSV at every project boundary.
                        await WriteRootDependenciesAsync(csvBuilder.ToString(), ct).ConfigureAwait(false);
                    }
                    break;
            }

            // Flush CSV after every completed project so results are visible on disk immediately.
            // Checkpoint cursor at the configured interval for resume support.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await WriteRootDependenciesAsync(csvBuilder.ToString(), ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;

                // Push aggregate metrics to Channel 2 snapshot store
                PushAggregateMetrics(metricsStore, allProjectStats, inventoryReport, organisations);
                PushSnapshot(snapshotStore, allProjectStats, inventoryReport, organisations);
            }
        }

        // Complete final organisation.
        if (currentOrg is not null)
        {
            // Flush the last org's CSV.
            if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
            {
                await WriteOrgDependenciesAsync(currentOrgFolder, currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);
                _logger.LogDebug("Flushed final org-level dependencies CSV for {Org}.", currentOrgFolder);
            }

            orgSw.Stop();
            var finalOrgTags = new MetricsTagList
            {
                { "job.id", job.JobId },
                { "module", ModuleName },
                { "organisation.url", currentOrg }
            };
            metrics?.SetProjectCount(orgProjectCount, finalOrgTags);
            metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, finalOrgTags);
            metrics?.OrganisationCompleted(finalOrgTags);
        }

        // Final write of root CSV.
        await WriteRootDependenciesAsync(csvBuilder.ToString(), ct).ConfigureAwait(false);

        // Generate grouped CSV, Mermaid diagrams, and per-project transitive graphs.
        await GenerateAnalysisOutputsAsync(csvBuilder.ToString(), ct).ConfigureAwait(false);

        // Emit ProjectComplete events for any configured project that did not receive one
        // during the main loop (e.g. projects with zero external links where the service
        // did not emit a heartbeat).
        foreach (var org in organisations)
        {
            foreach (var project in org.Projects)
            {
                var resolvedUrl = org.Endpoint.GetResolvedUrl();
                var normalizedKey = NormalizeProjectKey(resolvedUrl, project);
                if (!allCompletedProjects.Contains(normalizedKey))
                {
                    // Look up inventory for total work items
                    var totalWi = 0;
                    if (inventoryReport?.Organisations != null)
                    {
                        var invOrg = inventoryReport.Organisations
                            .FirstOrDefault(o => string.Equals(o.Url.TrimEnd('/'), resolvedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                        var invProj = invOrg?.Projects?.FirstOrDefault(p => string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase));
                        if (invProj != null)
                            totalWi = (int)Math.Min(invProj.WorkItems, int.MaxValue);
                    }

                    allCompletedProjects.Add(normalizedKey);
                    allProjectStats[normalizedKey] = new PerProjectStats(totalWi, 0, 0, 0, totalWi);

                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = "ProjectComplete",
                        Message = $"{resolvedUrl}|{project}",
                        Timestamp = DateTimeOffset.UtcNow,
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = totalWi },
                            Discovery = new DiscoveryCounters
                            {
                                Dependencies = new DependencyCounters
                                {
                                    WorkItemsAnalysed = totalWi,
                                    ExternalLinksFound = 0,
                                    CrossProjectLinks = 0,
                                    CrossOrgLinks = 0
                                }
                            }
                        }
                    });
                }
            }
        }

        // Final snapshot push
        PushAggregateMetrics(metricsStore, allProjectStats, inventoryReport, organisations);
        PushSnapshot(snapshotStore, allProjectStats, inventoryReport, organisations);

        jobSw.Stop();
        metrics?.RecordJobDuration(jobSw.Elapsed.TotalMilliseconds, new MetricsTagList
        {
            { "job.id", job.JobId },
            { "module", ModuleName }
        });

        sink.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Completed",
            Message = $"Dependency analysis complete. {recordCount} external links written.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation(
            "Dependencies module completed for job {JobId}. {RecordCount} records written.",
            job.JobId, recordCount);
    }

    /// <summary>
    /// Captures dependency links for a single org+project pair.
    /// Writes <c>discovery/{orgFolder}/{projectFolder}/dependencies.csv</c>.
    /// No cursor management — the plan executor handles task-level resume.
    /// </summary>
    public async Task<DependencyCounters> CaptureProjectAsync(
        IDependencyDiscoveryService dependencyService,
        InventoryContext context,
        JobPolicies policies,
        CancellationToken ct)
    {
        var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? string.Empty;
        var project = context.Project;
        var package = context.Package;
        var sink = context.ProgressSink ?? NullProgressSink.Instance;
        var job = context.Job;
        var checkpointInterval = TimeSpan.FromSeconds(Math.Max(0, policies.CheckpointIntervalSeconds));
        var checkpointing = _checkpointingFactory.Create(package);
        var checkpointIdentity = StateCursorIdentity.Build("dependencies", "dependencies");

        var orgFolder = PackagePathResolver.ExtractOrgFolderName(orgUrl);
        var projectSegment = PackagePathResolver.Sanitise(project);
        var projectFolder = $"{orgFolder}/{projectSegment}";
        var outputPath = $"{projectFolder}/dependencies.csv";

        using var activity = ActivitySource.StartActivity("capture.dependencies.project", ActivityKind.Internal);
        activity?.SetTag("job.id", job.JobId);
        activity?.SetTag("organisation.url", orgUrl);
        activity?.SetTag("project.name", project);

        _logger.LogInformation(
            "Capturing dependencies for project {Project} in {OrgUrl}.",
            project, orgUrl);

        sink.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Counting",
            Message = $"{orgUrl}|{project}",
            Timestamp = DateTimeOffset.UtcNow
        });

        const string CsvHeader =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemChangedDate,SourceWorkItemStateCategory";

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine(CsvHeader);

        int linksFound = 0;
        int crossProjectCount = 0;
        int crossOrgCount = 0;
        int workItemsAnalysed = 0;
        int totalWorkItems = 0;
        var savedCursor = await checkpointing.ReadCursorAsync(checkpointIdentity, ct).ConfigureAwait(false);
        var continuationToken = await checkpointing.ReadContinuationTokenAsync(checkpointIdentity, ct).ConfigureAwait(false);
        var resumedWorkItemsBaseline = savedCursor?.WorkItemsProcessed ?? 0;
        totalWorkItems = savedCursor?.TotalWorkItems ?? 0;
        var lastPersistedAt = DateTimeOffset.UtcNow;

        var existingCsv = await ReadProjectDependenciesAsync(orgFolder, projectSegment, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existingCsv))
        {
            LoadExistingProjectCsv(existingCsv!, csvBuilder, out linksFound, out crossProjectCount, out crossOrgCount);
        }

        async Task PersistProjectStateAsync(string stage, CancellationToken cancellationToken)
        {
            await WriteProjectDependenciesAsync(orgFolder, projectSegment, csvBuilder.ToString(), cancellationToken).ConfigureAwait(false);

            var cursor = new CursorEntry
            {
                LastProcessed = project,
                Stage = stage,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = workItemsAnalysed,
                TotalWorkItems = totalWorkItems,
                LastWorkItemId = continuationToken?.WorkItemId ?? savedCursor?.LastWorkItemId ?? 0
            };

            await checkpointing.WriteCursorAsync(checkpointIdentity, cursor, cancellationToken).ConfigureAwait(false);

            if (continuationToken is not null)
            {
                await checkpointing.WriteContinuationTokenAsync(checkpointIdentity, continuationToken, cancellationToken).ConfigureAwait(false);
            }

            lastPersistedAt = DateTimeOffset.UtcNow;
        }

        async Task PersistContinuationAsync(BatchContinuationToken token, CancellationToken cancellationToken)
        {
            continuationToken = token;

            if (checkpointInterval == TimeSpan.Zero || DateTimeOffset.UtcNow - lastPersistedAt >= checkpointInterval)
            {
                await PersistProjectStateAsync(CursorStage.CreatedOrUpdated, cancellationToken).ConfigureAwait(false);
            }
        }

        using var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);

        await foreach (var evt in dependencyService.DiscoverDependenciesAsync(
            inProgressProjectKey: continuationToken is not null ? NormalizeProjectKey(orgUrl, project) : null,
            inProgressToken: continuationToken,
            continuationCheckpointWriter: PersistContinuationAsync,
            cancellationToken: ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case DependencyFoundEvent found:
                    var r = found.Record;
                    var csvLine =
                        $"{r.SourceWorkItemId},{EscapeCsv(r.SourceWorkItemType ?? "")}," +
                        $"{EscapeCsv(r.SourceProject ?? "")},{EscapeCsv(r.SourceOrganisationUrl)}," +
                        $"{EscapeCsv(r.LinkType ?? "")},{r.LinkScope}," +
                        $"{r.TargetWorkItemId},{EscapeCsv(r.TargetProject ?? "")}," +
                        $"{EscapeCsv(r.TargetOrganisation ?? "")},{r.TargetStatus}," +
                        $"{(r.LinkChangedDate.HasValue ? r.LinkChangedDate.Value.ToString("O") : "")}," +
                        $"{(r.SourceWorkItemChangedDate.HasValue ? r.SourceWorkItemChangedDate.Value.ToString("O") : "")}," +
                        $"{EscapeCsv(r.SourceWorkItemStateCategory ?? "")}";
                    csvBuilder.AppendLine(csvLine);
                    linksFound++;
                    if (r.LinkScope == LinkScope.CrossProject) crossProjectCount++;
                    if (r.LinkScope == LinkScope.CrossOrganisation) crossOrgCount++;

                    _metrics?.RecordLinksFound(1, new MetricsTagList
                    {
                        { "job.id", job.JobId },
                        { "module", ModuleName },
                        { "organisation.url", r.SourceOrganisationUrl },
                        { "link.scope", r.LinkScope.ToString() }
                    });
                    break;

                case DependencyHeartbeatEvent heartbeat:
                    workItemsAnalysed = resumedWorkItemsBaseline + heartbeat.WorkItemsAnalysed;
                    totalWorkItems = heartbeat.TotalWorkItems;

                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = heartbeat.Error is not null
                            ? "Failed"
                            : heartbeat.IsComplete
                                ? "ProjectComplete"
                                : heartbeat.IsCounting
                                    ? "Counting"
                                    : "Analysis",
                        Message = $"{orgUrl}|{project}",
                        Timestamp = DateTimeOffset.UtcNow,
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = heartbeat.TotalWorkItems },
                            Discovery = new DiscoveryCounters
                            {
                                Dependencies = new DependencyCounters
                                {
                                    WorkItemsAnalysed = heartbeat.WorkItemsAnalysed,
                                    ExternalLinksFound = linksFound,
                                    CrossProjectLinks = crossProjectCount,
                                    CrossOrgLinks = crossOrgCount
                                }
                            }
                        }
                    });

                    if (!heartbeat.IsComplete &&
                        (checkpointInterval == TimeSpan.Zero || DateTimeOffset.UtcNow - lastPersistedAt >= checkpointInterval))
                    {
                        await PersistProjectStateAsync(CursorStage.CreatedOrUpdated, ct).ConfigureAwait(false);
                    }
                    break;
            }
        }

        await WriteProjectDependenciesAsync(orgFolder, projectSegment, csvBuilder.ToString(), ct).ConfigureAwait(false);
        await checkpointing.DeleteContinuationTokenAsync(checkpointIdentity, ct).ConfigureAwait(false);
        await checkpointing.WriteCursorAsync(checkpointIdentity, new CursorEntry
        {
            LastProcessed = project,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow,
            WorkItemsProcessed = workItemsAnalysed,
            TotalWorkItems = totalWorkItems,
            LastWorkItemId = continuationToken?.WorkItemId ?? savedCursor?.LastWorkItemId ?? 0
        }, ct).ConfigureAwait(false);

        if (linksFound == 0)
        {
            _logger.LogWarning(
                "Zero dependency links captured for project {Project} in {OrgUrl} — verify the project is reachable and contains linked work items.",
                project, orgUrl);
        }

        sink.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "ProjectComplete",
            Message = $"{orgUrl}|{project}",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Scope = new JobScopeCounters { WorkItemsTotal = totalWorkItems },
                Discovery = new DiscoveryCounters
                {
                    Dependencies = new DependencyCounters
                    {
                        WorkItemsAnalysed = workItemsAnalysed,
                        ExternalLinksFound = linksFound,
                        CrossProjectLinks = crossProjectCount,
                        CrossOrgLinks = crossOrgCount
                    }
                }
            }
        });

        _logger.LogInformation(
            "Captured {Links} dependency links for project {Project} in {OrgUrl}. Written to {Path}.",
            linksFound, project, orgUrl, outputPath);

        return new DependencyCounters
        {
            WorkItemsAnalysed = workItemsAnalysed,
            ExternalLinksFound = linksFound,
            CrossProjectLinks = crossProjectCount,
            CrossOrgLinks = crossOrgCount
        };
    }

    private static void LoadExistingProjectCsv(
        string existingCsv,
        StringBuilder csvBuilder,
        out int linksFound,
        out int crossProjectCount,
        out int crossOrgCount)
    {
        linksFound = 0;
        crossProjectCount = 0;
        crossOrgCount = 0;

        csvBuilder.Clear();

        var lines = existingCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            csvBuilder.AppendLine(line);

            var columns = ParseCsvLine(line);
            if (columns.Count < 6 || string.Equals(columns[0], "SourceWorkItemId", StringComparison.OrdinalIgnoreCase))
                continue;

            linksFound++;

            if (Enum.TryParse<LinkScope>(columns[5], true, out var linkScope))
            {
                if (linkScope == LinkScope.CrossProject)
                    crossProjectCount++;
                else if (linkScope == LinkScope.CrossOrganisation)
                    crossOrgCount++;
            }
        }
    }

    /// <summary>
    /// Pushes aggregate <see cref="JobMetrics"/> to the snapshot store (Channel 2)
    /// so the Control Plane telemetry endpoint reflects dependency discovery progress.
    /// </summary>
    private static void PushAggregateMetrics(
        IJobMetricsStore? metricsStore,
        Dictionary<string, PerProjectStats> allProjectStats,
        InventoryReport? inventoryReport,
        IReadOnlyList<ScopedOrganisationEndpoint> organisations)
    {
        if (metricsStore is null)
            return;

        long totalAnalysed = 0, totalLinks = 0, totalCrossProj = 0, totalCrossOrg = 0;
        int depProjectsCompleted = 0;

        foreach (var kvp in allProjectStats)
        {
            var stats = kvp.Value;
            totalAnalysed += stats.WorkItemsAnalysed;
            totalLinks += stats.ExternalLinksFound;
            totalCrossProj += stats.CrossProjectCount;
            totalCrossOrg += stats.CrossOrgCount;
            depProjectsCompleted++;
        }

        // Total projects from configuration
        int projectsTotal = 0;
        int orgsTotal = 0;
        foreach (var org in organisations)
        {
            orgsTotal++;
            projectsTotal += org.Projects.Count;
        }

        // Inventory data from the report (loaded at start)
        long totalWi = inventoryReport?.Totals.WorkItems ?? 0;
        long totalRev = inventoryReport?.Totals.Revisions ?? 0;
        int totalRepos = inventoryReport?.Totals.Repos ?? 0;

        metricsStore.Update(new JobMetrics
        {
            Timestamp = DateTimeOffset.UtcNow,
            Scope = new JobScopeCounters
            {
                OrganisationsTotal = orgsTotal,
                ProjectsTotal = projectsTotal,
                ProjectsCompleted = depProjectsCompleted,
                WorkItemsTotal = totalWi
            },
            Discovery = new DiscoveryCounters
            {
                Inventory = new InventoryCounters
                {
                    RevisionsTotal = totalRev,
                    RepositoriesTotal = totalRepos
                },
                Dependencies = new DependencyCounters
                {
                    WorkItemsAnalysed = totalAnalysed,
                    ExternalLinksFound = totalLinks,
                    CrossProjectLinks = totalCrossProj,
                    CrossOrgLinks = totalCrossOrg
                }
            }
        });
    }

    /// <summary>
    /// Pushes a <see cref="JobSnapshot"/> (Channel 3) with per-org/project dependency state.
    /// </summary>
    private static void PushSnapshot(
        IJobSnapshotStore? snapshotStore,
        Dictionary<string, PerProjectStats> allProjectStats,
        InventoryReport? inventoryReport,
        IReadOnlyList<ScopedOrganisationEndpoint> organisations)
    {
        if (snapshotStore is null)
            return;

        var orgSnapshots = new List<OrgSnapshot>();
        foreach (var org in organisations)
        {
            var orgUrl = org.Endpoint.GetResolvedUrl();
            var projectSnapshots = new List<ProjectSnapshot>();

            foreach (var proj in org.Projects)
            {
                var key = $"{orgUrl}|{proj}";
                var hasStats = allProjectStats.TryGetValue(key, out var stats);

                projectSnapshots.Add(new ProjectSnapshot
                {
                    Name = proj,
                    Status = hasStats ? ProjectStatus.Completed : ProjectStatus.Pending,
                    Discovery = new DiscoveryCounters
                    {
                        Dependencies = hasStats ? new DependencyCounters
                        {
                            WorkItemsAnalysed = stats!.WorkItemsAnalysed,
                            ExternalLinksFound = stats.ExternalLinksFound,
                            CrossProjectLinks = stats.CrossProjectCount,
                            CrossOrgLinks = stats.CrossOrgCount
                        } : null
                    }
                });
            }

            orgSnapshots.Add(new OrgSnapshot
            {
                Url = orgUrl,
                Name = orgUrl,
                Projects = projectSnapshots
            });
        }

        snapshotStore.Update(new JobSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Organisations = orgSnapshots
        });
    }

    /// <summary>
    /// Strips CSV rows that belong to a specific project (identified by org URL and project name)
    /// from the root CSV content. Used on resume to remove partial rows for an in-progress project
    /// before re-processing from the continuation token.
    /// </summary>
    internal static string StripCsvRowsForProject(string csvContent, string orgUrl, string projectName, out int strippedCount)
    {
        strippedCount = 0;
        var lines = csvContent.Split(new[] { '\n' }, StringSplitOptions.None);
        var result = new StringBuilder(csvContent.Length);
        var normalizedOrgUrl = orgUrl.TrimEnd('/').ToLowerInvariant();
        var normalizedProject = projectName.Trim().ToLowerInvariant();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                // Preserve blank lines (e.g., trailing newline)
                result.AppendLine();
                continue;
            }

            // Parse CSV columns — handle quoted fields
            var columns = ParseCsvLine(line);

            // Header row or non-data rows: keep them
            if (columns.Count < 4 || string.Equals(columns[0], "SourceWorkItemId", StringComparison.OrdinalIgnoreCase))
            {
                result.AppendLine(line);
                continue;
            }

            // Column 2 = SourceProject, Column 3 = SourceOrganisationUrl
            var rowProject = columns[2].Trim().ToLowerInvariant();
            var rowOrgUrl = columns[3].TrimEnd('/').Trim().ToLowerInvariant();

            if (string.Equals(rowProject, normalizedProject, StringComparison.Ordinal) &&
                string.Equals(rowOrgUrl, normalizedOrgUrl, StringComparison.Ordinal))
            {
                strippedCount++;
                continue; // Skip this row
            }

            result.AppendLine(line);
        }

        // Remove trailing blank line if the original didn't end with one
        var resultStr = result.ToString();
        if (!csvContent.EndsWith("\n") && resultStr.EndsWith(Environment.NewLine))
            resultStr = resultStr.TrimEnd('\r', '\n');

        return resultStr;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>
    /// Computes per-project dependency stats from the root <c>dependencies.csv</c> content.
    /// Returns a dictionary keyed by <c>"{orgUrl}|{project}"</c>.
    /// </summary>
    private static Dictionary<string, PerProjectStats> ComputePerProjectStatsFromCsv(
        string rootCsvContent, InventoryReport? inventory)
    {
        var stats = new Dictionary<string, PerProjectStats>(StringComparer.OrdinalIgnoreCase);
        var lines = rootCsvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Accumulate per-project link counts
        var analysed = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var externalLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var crossProjectLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var crossOrgLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var cols = ParseCsvLine(lines[i]);
            if (cols.Count < 6) continue;

            var sourceProject = cols[2];
            var sourceOrgUrl = cols[3];
            var linkScopeStr = cols[5];

            if (string.IsNullOrWhiteSpace(sourceProject) || string.IsNullOrWhiteSpace(sourceOrgUrl))
                continue;

            var key = NormalizeProjectKey(sourceOrgUrl, sourceProject);

            // Track unique work item IDs analysed
            if (int.TryParse(cols[0], out var wiId))
            {
                if (!analysed.TryGetValue(key, out var wiSet))
                {
                    wiSet = new HashSet<int>();
                    analysed[key] = wiSet;
                }
                wiSet.Add(wiId);
            }

            // Count links by scope
            if (!externalLinks.ContainsKey(key)) externalLinks[key] = 0;
            externalLinks[key]++;

            if (linkScopeStr.Equals("CrossOrganisation", StringComparison.OrdinalIgnoreCase))
            {
                if (!crossOrgLinks.ContainsKey(key)) crossOrgLinks[key] = 0;
                crossOrgLinks[key]++;
            }
            else
            {
                if (!crossProjectLinks.ContainsKey(key)) crossProjectLinks[key] = 0;
                crossProjectLinks[key]++;
            }
        }

        // Build per-project work item total lookup from inventory
        var inventoryWorkItems = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (inventory?.Organisations != null)
        {
            foreach (var org in inventory.Organisations)
            {
                foreach (var proj in org.Projects)
                {
                    var key = NormalizeProjectKey(org.Url, proj.Name);
                    inventoryWorkItems[key] = (int)Math.Min(proj.WorkItems, int.MaxValue);
                }
            }
        }

        // Merge all keys
        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in analysed.Keys) allKeys.Add(k);
        foreach (var k in externalLinks.Keys) allKeys.Add(k);

        foreach (var key in allKeys)
        {
            var wiWithLinks = analysed.TryGetValue(key, out var wiSet) ? wiSet.Count : 0;
            var extLinks = externalLinks.TryGetValue(key, out var el) ? el : 0;
            var cpLinks = crossProjectLinks.TryGetValue(key, out var cp) ? cp : 0;
            var coLinks = crossOrgLinks.TryGetValue(key, out var co) ? co : 0;
            var totalWi = inventoryWorkItems.TryGetValue(key, out var invWi) ? invWi : wiWithLinks;
            // For reconciled (completed) projects, all work items were analysed.
            var wiAnalysed = totalWi > 0 ? totalWi : wiWithLinks;

            stats[key] = new PerProjectStats(wiAnalysed, extLinks, cpLinks, coLinks, totalWi);
        }

        return stats;
    }

    /// <summary>
    /// Regenerates per-org and per-project <c>dependencies.csv</c> files from the root CSV.
    /// </summary>
    private async Task RegenerateOutputFilesFromRootCsvAsync(
        string rootCsvContent, CancellationToken ct)
    {
        const string CsvHeader =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemChangedDate,SourceWorkItemStateCategory";

        // Parse root CSV into per-org and per-project buckets
        var perOrg = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var perProject = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

        var lines = rootCsvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var cols = ParseCsvLine(lines[i]);
            if (cols.Count < 4) continue;

            var sourceProject = cols[2];
            var sourceOrgUrl = cols[3];
            if (string.IsNullOrWhiteSpace(sourceProject) || string.IsNullOrWhiteSpace(sourceOrgUrl))
                continue;

            var orgFolder = PackagePathResolver.ExtractOrgFolderName(sourceOrgUrl);
            var projectFolder = $"{orgFolder}/{PackagePathResolver.Sanitise(sourceProject)}";
            var rawLine = lines[i].TrimEnd('\r');

            // Per-org accumulator
            if (!perOrg.TryGetValue(orgFolder, out var orgSb))
            {
                orgSb = new StringBuilder();
                orgSb.AppendLine(CsvHeader);
                perOrg[orgFolder] = orgSb;
            }
            orgSb.AppendLine(rawLine);

            // Per-project accumulator
            if (!perProject.TryGetValue(projectFolder, out var projSb))
            {
                projSb = new StringBuilder();
                projSb.AppendLine(CsvHeader);
                perProject[projectFolder] = projSb;
            }
            projSb.AppendLine(rawLine);
        }

        // Write root CSV (ensures it exists even if it was somehow deleted)
        await WriteRootDependenciesAsync(rootCsvContent, ct).ConfigureAwait(false);

        // Write per-org CSVs (keyed by org folder)
        foreach (var kvp in perOrg)
        {
            await WriteOrgDependenciesAsync(kvp.Key, kvp.Value.ToString(), ct).ConfigureAwait(false);
            _logger.LogInformation("Regenerated org-level dependencies CSV for {Org}.", kvp.Key);
        }

        // Write per-project CSVs (keyed by "{orgFolder}/{project}")
        foreach (var kvp in perProject)
        {
            var (projOrgFolder, projProjectFolder) = SplitProjectFolderKey(kvp.Key);
            await WriteProjectDependenciesAsync(projOrgFolder, projProjectFolder, kvp.Value.ToString(), ct).ConfigureAwait(false);
            _logger.LogDebug("Regenerated per-project dependencies CSV for {Project}.", kvp.Key);
        }

        _logger.LogInformation(
            "Regenerated {OrgCount} org-level and {ProjectCount} project-level CSV files from root dependencies.csv.",
            perOrg.Count, perProject.Count);
    }

    /// <summary>
    /// Generates the analysis outputs from the root <c>dependencies.csv</c>.
    /// </summary>
    private async Task GenerateAnalysisOutputsAsync(
        string rootCsvContent, CancellationToken ct)
    {
        // ── Step 1: Parse root CSV into grouped project-pair records ─────
        var pairAccumulator = new Dictionary<string, (string SourceProject, string TargetProject, string TargetOrganisation, LinkScope Scope, int Count, DateTimeOffset? MostRecentLinkDate, DateTimeOffset? MostRecentSourceWorkItemChangedDate)>(
            StringComparer.OrdinalIgnoreCase);

        var lines = rootCsvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var cols = ParseCsvLine(lines[i]);
            if (cols.Count < 9) continue;

            var sourceProject = cols[2];
            var sourceOrgUrl = cols[3];
            var linkScopeStr = cols[5];
            var targetProject = cols[7];
            var targetOrg = cols[8];
            var linkChangedDate = cols.Count > 10 ? ParseOptionalDate(cols[10]) : null;
            var sourceWorkItemChangedDate = cols.Count > 11 ? ParseOptionalDate(cols[11]) : null;

            if (string.IsNullOrWhiteSpace(sourceProject)) continue;

            LinkScope scope;
            if (linkScopeStr.Equals("CrossOrganisation", StringComparison.OrdinalIgnoreCase))
                scope = LinkScope.CrossOrganisation;
            else
                scope = LinkScope.CrossProject;

            var pairKey = $"{sourceProject}|{targetProject}|{targetOrg}|{scope}";
            if (pairAccumulator.TryGetValue(pairKey, out var existing))
            {
                pairAccumulator[pairKey] = (
                    existing.SourceProject,
                    existing.TargetProject,
                    existing.TargetOrganisation,
                    existing.Scope,
                    existing.Count + 1,
                    MaxDate(existing.MostRecentLinkDate, linkChangedDate),
                    MaxDate(existing.MostRecentSourceWorkItemChangedDate, sourceWorkItemChangedDate));
            }
            else
            {
                pairAccumulator[pairKey] = (sourceProject, targetProject, targetOrg, scope, 1, linkChangedDate, sourceWorkItemChangedDate);
            }
        }

        if (pairAccumulator.Count == 0)
        {
            _logger.LogInformation("No external dependencies found — skipping analysis output generation.");
            return;
        }

        // Build ProjectDependencyRecord list
        var records = new List<ProjectDependencyRecord>();
        foreach (var kvp in pairAccumulator)
        {
            var (sourceProject, targetProject, targetOrg, scope, count, mostRecentLinkDate, mostRecentSourceWorkItemChangedDate) = kvp.Value;
            records.Add(new ProjectDependencyRecord
            {
                SourceProject = sourceProject,
                TargetProject = targetProject,
                TargetOrganisation = targetOrg,
                LinkCount = count,
                LinkScope = scope,
                MostRecentLinkDate = mostRecentLinkDate,
                MostRecentSourceWorkItemChangedDate = mostRecentSourceWorkItemChangedDate
            });
        }

        // Assign component IDs via Union-Find
        var componentIds = UnionFindComponentLabeler.AssignComponentIds(records);
        foreach (var rec in records)
        {
            if (componentIds.TryGetValue(rec.SourceProject, out var groupId))
                rec.GroupId = groupId;
        }

        // Sort by LinkCount descending for the CSV output
        records.Sort((a, b) => b.LinkCount.CompareTo(a.LinkCount));

        // ── Step 2: Write discovery-project-dependencies.csv ─────────────
        var groupedCsv = new StringBuilder();
        groupedCsv.AppendLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId,MostRecentLinkDate,MostRecentSourceWorkItemChangedDate");
        foreach (var rec in records)
        {
            groupedCsv.AppendLine(
                $"{EscapeCsv(rec.SourceProject)},{EscapeCsv(rec.TargetProject)},{EscapeCsv(rec.TargetOrganisation)}," +
            $"{rec.LinkCount},{rec.LinkScope},{rec.GroupId}," +
            $"{FormatOptionalDate(rec.MostRecentLinkDate)},{FormatOptionalDate(rec.MostRecentSourceWorkItemChangedDate)}");
        }
        await WriteIndexTextAsync(new PackageIndexContext("discovery-project-dependencies.csv"), groupedCsv.ToString(), ct).ConfigureAwait(false);
        _logger.LogInformation("Wrote discovery-project-dependencies.csv with {PairCount} project pairs.", records.Count);

        // ── Step 3: Write overall Mermaid diagram ────────────────────────
        var diagramBuilder = new MermaidDiagramBuilder(records);
        var mermaidContent = new StringBuilder();
        mermaidContent.AppendLine("# Project Dependency Graph");
        mermaidContent.AppendLine();
        mermaidContent.AppendLine("```mermaid");
        mermaidContent.Append(diagramBuilder.Build());
        mermaidContent.AppendLine("```");
        await WriteIndexTextAsync(new PackageIndexContext("discovery-project-dependencies.md"), mermaidContent.ToString(), ct).ConfigureAwait(false);
        _logger.LogInformation("Wrote discovery-project-dependencies.md (overall Mermaid diagram).");

        // ── Step 4: Build grouped data for transitive walker ─────────────
        var groupedData = new Dictionary<string, List<TransitiveDependencyWalker.GroupedRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in records)
        {
            // Determine the org folder for this source project from the root CSV
            var orgFolder = FindOrgFolderForProject(lines, rec.SourceProject);
            if (orgFolder == null) continue;

            var key = $"{orgFolder}/{rec.SourceProject}";
            if (!groupedData.TryGetValue(key, out var rowList))
            {
                rowList = new List<TransitiveDependencyWalker.GroupedRow>();
                groupedData[key] = rowList;
            }
            rowList.Add(new TransitiveDependencyWalker.GroupedRow
            {
                SourceProject = rec.SourceProject,
                TargetProject = rec.TargetProject,
                TargetOrganisation = rec.TargetOrganisation,
                LinkCount = rec.LinkCount,
                LinkScope = rec.LinkScope
            });
        }

        // ── Step 5: Write per-project transitive dependency graphs ───────
        var walker = new TransitiveDependencyWalker(groupedData);
        var writtenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in groupedData.Keys)
        {
            // key is "{orgFolder}/{project}"
            var slashIdx = key.IndexOf('/');
            if (slashIdx < 0) continue;

            var orgName = key.Substring(0, slashIdx);
            var projectName = key.Substring(slashIdx + 1);

            if (!writtenProjects.Add(key)) continue;

            var walkResult = walker.Walk(orgName, projectName, maxDepth: 3);
            var transitiveBuilder = new TransitiveMermaidBuilder(walkResult, projectName);

            var projectMermaid = new StringBuilder();
            projectMermaid.AppendLine($"# Dependency Graph: {projectName}");
            projectMermaid.AppendLine();
            projectMermaid.AppendLine("```mermaid");
            projectMermaid.Append(transitiveBuilder.Build());
            projectMermaid.AppendLine("```");

            await WriteIndexTextAsync(new PackageIndexContext("dependency-graph.md", Organisation: orgName, Project: projectName), projectMermaid.ToString(), ct).ConfigureAwait(false);
            _logger.LogDebug("Wrote transitive dependency graph for {Project}.", key);
        }

        _logger.LogInformation(
            "Generated analysis outputs: {PairCount} grouped pairs, {ProjectCount} per-project graphs.",
            records.Count, writtenProjects.Count);
    }

    private static DateTimeOffset? ParseOptionalDate(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static DateTimeOffset? MaxDate(DateTimeOffset? left, DateTimeOffset? right)
        => left is null ? right : right is null ? left : left >= right ? left : right;

    private static string FormatOptionalDate(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToString("O") : string.Empty;

    /// <summary>
    /// Scans root CSV lines to find the organisation URL for a given source project,
    /// then converts it to an org folder name.
    /// </summary>
    private static string? FindOrgFolderForProject(string[] csvLines, string sourceProject)
    {
        for (int i = 1; i < csvLines.Length; i++)
        {
            var cols = ParseCsvLine(csvLines[i]);
            if (cols.Count >= 4 &&
                string.Equals(cols[2], sourceProject, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cols[3]))
            {
                return PackagePathResolver.ExtractOrgFolderName(cols[3]);
            }
        }
        return null;
    }

    /// <summary>
    /// Splits a <c>"{orgFolder}/{project}"</c> bucket key into its organisation folder and
    /// project segments for routing through the project-scoped package content kinds.
    /// </summary>
    private static (string OrgFolder, string ProjectFolder) SplitProjectFolderKey(string key)
    {
        var slashIdx = key.IndexOf('/');
        return slashIdx < 0
            ? (key, string.Empty)
            : (key.Substring(0, slashIdx), key.Substring(slashIdx + 1));
    }

    /// <summary>
    /// Parses a single CSV line respecting quoted fields (RFC 4180).
    /// </summary>
    internal static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    // Escaped quote ("") or end of quoted field
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // skip the second quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (ch != '\r')
                {
                    current.Append(ch);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private async Task<string?> ReadIndexTextAsync(PackageIndexContext context, CancellationToken ct)
    {
        if (_package is null)
            throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for package content operations.");

        var payload = await _package.RequestIndexAsync(context, ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task WriteIndexTextAsync(PackageIndexContext context, string content, CancellationToken ct)
    {
        if (_package is null)
            throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for package content operations.");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await _package.PersistIndexAsync(
            context,
            new PackagePayload(stream, "text/plain"),
            ct).ConfigureAwait(false);
    }

    // ── Typed routing helpers for the structural dependency outputs ──────────
    // Scope flows through the PackageIndexContext (Organisation/Project), not an
    // address, so the package router maps each structural file to its canonical path.
    private Task WriteRootDependenciesAsync(string content, CancellationToken ct)
        => WriteIndexTextAsync(new PackageIndexContext("dependencies.csv"), content, ct);

    private Task WriteOrgDependenciesAsync(string orgFolder, string content, CancellationToken ct)
        => WriteIndexTextAsync(new PackageIndexContext("dependencies.csv", Organisation: orgFolder), content, ct);

    private Task WriteProjectDependenciesAsync(string orgFolder, string projectFolder, string content, CancellationToken ct)
        => WriteIndexTextAsync(new PackageIndexContext("dependencies.csv", Organisation: orgFolder, Project: projectFolder), content, ct);

    private Task<string?> ReadProjectDependenciesAsync(string orgFolder, string projectFolder, CancellationToken ct)
        => ReadIndexTextAsync(new PackageIndexContext("dependencies.csv", Organisation: orgFolder, Project: projectFolder), ct);

    private sealed class OrganisationMigrationEndpointOptions : MigrationEndpointOptions
    {
        private readonly OrganisationEndpoint _endpoint;

        public OrganisationMigrationEndpointOptions(OrganisationEndpoint endpoint)
        {
            _endpoint = endpoint;
            Type = endpoint.Type;
        }

        public override OrganisationEndpoint ToOrganisationEndpoint() => _endpoint;
        public override string GetResolvedUrl() => _endpoint.ResolvedUrl;
    }

    private sealed class NullProgressSink : IProgressSink
    {
        public static readonly NullProgressSink Instance = new();
        public void Emit(ProgressEvent evt) { }
    }

    private sealed record PerProjectStats(
        int WorkItemsAnalysed,
        int ExternalLinksFound,
        int CrossProjectCount,
        int CrossOrgCount,
        int TotalWorkItems);
}

