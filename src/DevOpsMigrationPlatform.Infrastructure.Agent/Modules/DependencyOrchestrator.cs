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
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
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
    private static readonly string CursorKey = PackagePaths.CursorFile("DependencyDiscovery");
    private const string RootCsvPath = "dependencies.csv";
    private const string ModuleName = "Dependencies";

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _metrics;

    public DependencyOrchestrator(ILogger<DependencyOrchestrator> logger, IPlatformMetrics? metrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
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
        var store = context.ArtefactStore;
        var state = context.StateStore;
        var sink = context.ProgressSink ?? NullProgressSink.Instance;
        IJobMetricsStore? metricsStore = null;
        IJobSnapshotStore? snapshotStore = null;

        _logger.LogInformation("Dependencies module starting for job {JobId}.", job.JobId);

        // ── Pre-count: load inventory.json for grand totals ──────────────────
        long grandTotalWorkItems = 0;
        InventoryReport? inventoryReport = null;
        var inventoryJson = await store.ReadAsync("inventory.json", ct).ConfigureAwait(false);
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

        var cursorJson = await state.ReadAsync(CursorKey, ct).ConfigureAwait(false);

        // Legacy fallback: capitalised filename in .migration/Checkpoints/ (pre-standardisation).
        if (cursorJson is null)
            cursorJson = await state.ReadAsync(PackagePaths.Checkpoints + "/Dependencies.cursor.json", ct).ConfigureAwait(false);

        // Legacy fallback: pre-.migration path for old packages.
        if (cursorJson is null)
            cursorJson = await state.ReadAsync("Checkpoints/Dependencies.cursor.json", ct).ConfigureAwait(false);

        if (cursorJson is not null)
        {
            _logger.LogInformation("Found existing dependencies cursor — attempting resume.");

            // Parse completed project keys and per-project stats from cursor
            try
            {
                using var doc = JsonDocument.Parse(cursorJson);
                if (doc.RootElement.TryGetProperty("completedProjects", out var projArray))
                {
                    foreach (var item in projArray.EnumerateArray())
                    {
                        var key = item.GetString();
                        if (key is not null)
                            completedProjects.Add(key);
                    }
                }
                if (doc.RootElement.TryGetProperty("recordCount", out var rc))
                    recordCount = rc.GetInt32();

                // Parse per-project stats if present (added for resume display)
                if (doc.RootElement.TryGetProperty("projectStats", out var statsObj) &&
                    statsObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in statsObj.EnumerateObject())
                    {
                        var s = prop.Value;
                        resumedProjectStats[prop.Name] = new PerProjectStats(
                            WorkItemsAnalysed: s.TryGetProperty("workItemsAnalysed", out var wa) ? wa.GetInt32() : 0,
                            ExternalLinksFound: s.TryGetProperty("externalLinksFound", out var elf) ? elf.GetInt32() : 0,
                            CrossProjectCount: s.TryGetProperty("crossProjectCount", out var cpc) ? cpc.GetInt32() : 0,
                            CrossOrgCount: s.TryGetProperty("crossOrgCount", out var coc) ? coc.GetInt32() : 0,
                            TotalWorkItems: s.TryGetProperty("totalWorkItems", out var twi) ? twi.GetInt32() : 0
                        );
                    }
                }

                // Parse in-progress project state for batch-level resume
                if (doc.RootElement.TryGetProperty("inProgressProject", out var ipObj) &&
                    ipObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (ipObj.TryGetProperty("key", out var ipKey))
                        inProgressProjectKey = ipKey.GetString();

                    if (inProgressProjectKey is not null && ipObj.TryGetProperty("continuationToken", out var ctObj))
                    {
                        try
                        {
                            inProgressToken = JsonSerializer.Deserialize<BatchContinuationToken>(ctObj.GetRawText());
                        }
                        catch (JsonException)
                        {
                            _logger.LogWarning("Failed to deserialize in-progress continuation token — project will restart from beginning.");
                            inProgressToken = null;
                        }
                    }

                    if (ipObj.TryGetProperty("processedWorkItems", out var ipWi))
                        inProgressProcessedWorkItems = ipWi.GetInt32();
                    if (ipObj.TryGetProperty("linksFound", out var ipLf))
                        inProgressLinksFound = ipLf.GetInt32();
                    if (ipObj.TryGetProperty("crossProjectCount", out var ipCp))
                        inProgressCrossProjectCount = ipCp.GetInt32();
                    if (ipObj.TryGetProperty("crossOrgCount", out var ipCo))
                        inProgressCrossOrgCount = ipCo.GetInt32();

                    if (inProgressProjectKey is not null)
                        _logger.LogInformation(
                            "Found in-progress project {ProjectKey} with {ProcessedItems} items already processed — will attempt batch-level resume.",
                            inProgressProjectKey, inProgressProcessedWorkItems);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse dependencies cursor — starting fresh.");
                completedProjects.Clear();
                resumedProjectStats.Clear();
                recordCount = 0;
            }

            // Reload existing CSV so we append rather than overwrite
            if (completedProjects.Count > 0 || inProgressProjectKey is not null)
            {
                var existingCsv = await store.ReadAsync(RootCsvPath, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(existingCsv))
                {
                    // If resuming an in-progress project, strip its partial CSV rows
                    // to avoid duplicates when the project is re-processed from the
                    // continuation token point.
                    if (inProgressProjectKey is not null)
                    {
                        var sep = inProgressProjectKey.IndexOf('|');
                        if (sep > 0)
                        {
                            var ipOrgUrl = inProgressProjectKey.Substring(0, sep);
                            var ipProject = inProgressProjectKey.Substring(sep + 1);
                            var strippedCsv = StripCsvRowsForProject(existingCsv!, ipOrgUrl, ipProject, out var strippedCount);
                            existingCsvRows.Append(strippedCsv);
                            recordCount -= strippedCount;
                            if (recordCount < 0) recordCount = 0;
                            _logger.LogInformation(
                                "Stripped {StrippedCount} partial CSV rows for in-progress project {ProjectKey} before resume.",
                                strippedCount, inProgressProjectKey);
                        }
                        else
                        {
                            existingCsvRows.Append(existingCsv);
                        }
                    }
                    else
                    {
                        existingCsvRows.Append(existingCsv);
                    }

                    _logger.LogInformation(
                        "Resuming dependency analysis — {CompletedCount} project(s) already completed, {RecordCount} records loaded.",
                        completedProjects.Count, recordCount);
                }

                // Emit synthetic ProjectComplete events for previously-completed projects so
                // the CLI live table immediately shows the correct counts on resume.
                foreach (var projectKey in completedProjects)
                {
                    var separatorIndex = projectKey.IndexOf('|');
                    if (separatorIndex < 0)
                        continue;

                    var orgUrl = projectKey.Substring(0, separatorIndex);
                    var projName = projectKey.Substring(separatorIndex + 1);
                    resumedProjectStats.TryGetValue(projectKey, out var stats);
                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = "ProjectComplete",
                        Message = $"{orgUrl}|{projName}",
                        Timestamp = DateTimeOffset.UtcNow,
                        Metrics = stats is not null ? new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = stats.TotalWorkItems },
                            Discovery = new DiscoveryCounters
                            {
                                Dependencies = new DependencyCounters
                                {
                                    WorkItemsAnalysed = stats.WorkItemsAnalysed,
                                    ExternalLinksFound = stats.ExternalLinksFound,
                                    CrossProjectLinks = stats.CrossProjectCount,
                                    CrossOrgLinks = stats.CrossOrgCount
                                }
                            }
                        } : null
                    });
                }
            }
        }
        else
        {
            // ── Automatic reconciliation: rebuild cursor from existing CSV ────
            // If the cursor file is missing but dependencies.csv exists, the
            // checkpoint was lost (crash, manual deletion, corruption). Rather
            // than re-analysing every project from scratch, parse the CSV to
            // discover which projects were already completed.
            var existingCsv = await store.ReadAsync(RootCsvPath, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(existingCsv))
            {
                _logger.LogWarning(
                    "No dependencies cursor found but dependencies.csv exists — reconciling checkpoint from CSV data.");

                var lines = existingCsv!.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                // Map from normalized key → original-casing key for display purposes.
                var reconciledDisplayKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Skip header row; extract SourceOrganisationUrl (col 3) and SourceProject (col 2)
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = ParseCsvLine(lines[i]);
                    if (cols.Count >= 4)
                    {
                        var sourceProject = cols[2];
                        var sourceOrgUrl = cols[3];
                        if (!string.IsNullOrWhiteSpace(sourceProject) && !string.IsNullOrWhiteSpace(sourceOrgUrl))
                        {
                            var normalized = NormalizeProjectKey(sourceOrgUrl, sourceProject);
                            completedProjects.Add(normalized);
                            if (!reconciledDisplayKeys.ContainsKey(normalized))
                                reconciledDisplayKeys[normalized] = $"{sourceOrgUrl}|{sourceProject}";
                        }
                    }
                    recordCount++;
                }

                if (completedProjects.Count > 0)
                {
                    existingCsvRows.Append(existingCsv);

                    // Compute per-project stats from CSV so we can emit accurate synthetic events
                    // and persist them in the cursor for future resume display.
                    var reconciledStats = ComputePerProjectStatsFromCsv(existingCsv!, inventoryReport);

                    // Persist the reconciled cursor so the next restart doesn't re-reconcile
                    await WriteCursorAsync(state, recordCount, completedProjects, reconciledStats, ct: ct).ConfigureAwait(false);

                    _logger.LogWarning(
                        "Reconciled dependencies cursor from CSV — {CompletedCount} project(s), {RecordCount} records. " +
                        "Projects already analysed will be skipped on this run.",
                        completedProjects.Count, recordCount);

                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = "Reconciled",
                        Message = $"Checkpoint reconciled from existing CSV: {completedProjects.Count} projects, {recordCount} records.",
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    // Emit synthetic ProjectComplete events for reconciled projects so the
                    // CLI live table immediately shows the correct counts.
                    foreach (var projectKey in completedProjects)
                    {
                        var separatorIndex = projectKey.IndexOf('|');
                        if (separatorIndex < 0)
                            continue;

                        var displayKey = reconciledDisplayKeys.TryGetValue(projectKey, out var dk) ? dk : projectKey;
                        reconciledStats.TryGetValue(projectKey, out var stats);
                        sink.Emit(new ProgressEvent
                        {
                            Module = ModuleName,
                            Stage = "ProjectComplete",
                            Message = displayKey,
                            Timestamp = DateTimeOffset.UtcNow,
                            Metrics = stats is not null ? new JobMetrics
                            {
                                Scope = new JobScopeCounters { WorkItemsTotal = stats.TotalWorkItems },
                                Discovery = new DiscoveryCounters
                                {
                                    Dependencies = new DependencyCounters
                                    {
                                        WorkItemsAnalysed = stats.WorkItemsAnalysed,
                                        ExternalLinksFound = stats.ExternalLinksFound,
                                        CrossProjectLinks = stats.CrossProjectCount,
                                        CrossOrgLinks = stats.CrossOrgCount
                                    }
                                }
                            } : null
                        });
                    }
                }
            }
        }

        // Build the CSV — either from existing data or fresh header
        var csvBuilder = new StringBuilder();
        if (existingCsvRows.Length > 0)
        {
            csvBuilder.Append(existingCsvRows);
        }
        else
        {
            csvBuilder.AppendLine(
                "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
                "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory");
        }

        // ── Early completion: if all configured projects are already done, regenerate
        // any missing per-org/per-project CSVs from root CSV and exit without re-running discovery.
        if (completedProjects.Count > 0)
        {
            var allProjectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Keep a mapping from normalized key → display-friendly key for event emission.
            var displayKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var org in organisations)
            {
                foreach (var project in org.Projects)
                {
                    var resolvedUrl = org.Endpoint.GetResolvedUrl();
                    var normalizedKey = NormalizeProjectKey(resolvedUrl, project);
                    allProjectKeys.Add(normalizedKey);
                    // Prefer the original casing for display.
                    if (!displayKeys.ContainsKey(normalizedKey))
                        displayKeys[normalizedKey] = $"{resolvedUrl}|{project}";
                }
            }

            // Check if every configured project is already completed
            if (allProjectKeys.Count > 0 && allProjectKeys.IsSubsetOf(completedProjects))
            {
                _logger.LogInformation(
                    "All {ProjectCount} configured project(s) are already completed — regenerating output files from existing data.",
                    allProjectKeys.Count);

                await RegenerateOutputFilesFromRootCsvAsync(store, csvBuilder.ToString(), ct).ConfigureAwait(false);

                // Generate grouped CSV, Mermaid diagrams, and per-project transitive graphs.
                await GenerateAnalysisOutputsAsync(store, csvBuilder.ToString(), ct).ConfigureAwait(false);

                // Emit per-project ProjectComplete events so the CLI/TUI can display stats.
                var perProjectStats = ComputePerProjectStatsFromCsv(csvBuilder.ToString(), inventoryReport);

                // Ensure every configured project gets an event, even if it had zero links.
                foreach (var projectKey in allProjectKeys)
                {
                    var displayKey = displayKeys.TryGetValue(projectKey, out var dk) ? dk : projectKey;

                    if (!perProjectStats.TryGetValue(projectKey, out var stats))
                    {
                        // Project had zero external links — still emit completion with inventory total if available.
                        var totalWi = 0;
                        if (inventoryReport?.Organisations != null)
                        {
                            var sep = projectKey.IndexOf('|');
                            if (sep > 0)
                            {
                                var orgUrl = projectKey.Substring(0, sep);
                                var projName = projectKey.Substring(sep + 1);
                                var org = inventoryReport.Organisations
                                    .FirstOrDefault(o => string.Equals(o.Url.TrimEnd('/'), orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                                var proj = org?.Projects?.FirstOrDefault(p => string.Equals(p.Name, projName, StringComparison.OrdinalIgnoreCase));
                                if (proj != null)
                                    totalWi = (int)Math.Min(proj.WorkItems, int.MaxValue);
                            }
                        }
                        stats = new PerProjectStats(totalWi, 0, 0, 0, totalWi);
                    }

                    sink.Emit(new ProgressEvent
                    {
                        Module = ModuleName,
                        Stage = "ProjectComplete",
                        Message = displayKey,
                        Timestamp = DateTimeOffset.UtcNow,
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = stats.TotalWorkItems },
                            Discovery = new DiscoveryCounters
                            {
                                Dependencies = new DependencyCounters
                                {
                                    WorkItemsAnalysed = stats.WorkItemsAnalysed,
                                    ExternalLinksFound = stats.ExternalLinksFound,
                                    CrossProjectLinks = stats.CrossProjectCount,
                                    CrossOrgLinks = stats.CrossOrgCount
                                }
                            }
                        }
                    });
                }

                // Clean up the cursor — analysis is fully complete
                await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

                // Push aggregate metrics for the reconciled-complete path
                PushAggregateMetrics(metricsStore, perProjectStats, inventoryReport, organisations);
                PushSnapshot(snapshotStore, perProjectStats, inventoryReport, organisations);

                sink.Emit(new ProgressEvent
                {
                    Module = ModuleName,
                    Stage = "Completed",
                    Message = $"Dependency analysis already complete (reconciled). {recordCount} external links verified.",
                    Timestamp = DateTimeOffset.UtcNow
                });

                _logger.LogInformation(
                    "Dependencies module completed for job {JobId} (reconciled). {RecordCount} records verified.",
                    job.JobId, recordCount);
                return;
            }
        }

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
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory";

        // Track in-progress project state for checkpoint writes
        string? currentInProgressKey = inProgressProjectKey;
        BatchContinuationToken? currentInProgressToken = inProgressToken;
        int currentInProgressProcessed = inProgressProcessedWorkItems;
        int currentInProgressLinks = inProgressLinksFound;
        int currentInProgressCrossProject = inProgressCrossProjectCount;
        int currentInProgressCrossOrg = inProgressCrossOrgCount;

        // Checkpoint writer callback invoked by IWorkItemFetchService on each batch boundary.
        // Updates the in-memory token and persists the cursor with in-progress state.
        async Task OnBatchCheckpoint(BatchContinuationToken token, CancellationToken checkpointCt)
        {
            currentInProgressToken = token;
            await WriteCursorAsync(state, recordCount, allCompletedProjects, allProjectStats,
                currentInProgressKey, currentInProgressToken, currentInProgressProcessed,
                currentInProgressLinks, currentInProgressCrossProject, currentInProgressCrossOrg,
                checkpointCt).ConfigureAwait(false);
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
                            await store.WriteAsync($"{currentOrgFolder}/dependencies.csv", currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);
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
                        Stage = heartbeat.Error is not null ? "Failed" : (heartbeat.IsComplete ? "ProjectComplete" : "Analysis"),
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
                            await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);

                            // Also flush the current project's partial CSV.
                            var midOrgFolder = PackagePathResolver.ExtractOrgFolderName(heartbeat.OrganisationUrl);
                            var midProjectFolder = $"{midOrgFolder}/{PackagePathResolver.Sanitise(heartbeat.ProjectName)}";
                            if (perProjectCsv.TryGetValue(midProjectFolder, out var midProjCsv))
                                await store.WriteAsync($"{midProjectFolder}/dependencies.csv", midProjCsv.ToString(), ct).ConfigureAwait(false);

                            // Also flush the current org's partial CSV.
                            if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
                                await store.WriteAsync($"{currentOrgFolder}/dependencies.csv", currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);

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
                        var hbProjectFolder = $"{hbOrgFolder}/{PackagePathResolver.Sanitise(heartbeat.ProjectName)}";

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
                            await store.WriteAsync($"{hbProjectFolder}/dependencies.csv", completedProjCsv.ToString(), ct).ConfigureAwait(false);
                            _logger.LogDebug("Flushed per-project dependencies CSV for {Project}.", hbProjectFolder);
                        }

                        // Flush org-level CSV at every project boundary so it stays current.
                        if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
                        {
                            await store.WriteAsync($"{currentOrgFolder}/dependencies.csv", currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);
                            _logger.LogDebug("Flushed org-level dependencies CSV for {Org} at project boundary.", currentOrgFolder);
                        }

                        // Flush root CSV at every project boundary.
                        await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                    }
                    break;
            }

            // Flush CSV after every completed project so results are visible on disk immediately.
            // Checkpoint cursor at the configured interval for resume support.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                await WriteCursorAsync(state, recordCount, allCompletedProjects, allProjectStats,
                    currentInProgressKey, currentInProgressToken, currentInProgressProcessed,
                    currentInProgressLinks, currentInProgressCrossProject, currentInProgressCrossOrg, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                metrics?.RecordCheckpointSaved(new MetricsTagList { { "job.id", job.JobId }, { "module", ModuleName } });
                _logger.LogDebug("Dependencies checkpoint saved at {RecordCount} records, {CompletedProjects} projects completed.",
                    recordCount, allCompletedProjects.Count);

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
                await store.WriteAsync($"{currentOrgFolder}/dependencies.csv", currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);
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
        await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);

        // Generate grouped CSV, Mermaid diagrams, and per-project transitive graphs.
        await GenerateAnalysisOutputsAsync(store, csvBuilder.ToString(), ct).ConfigureAwait(false);

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

        await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

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

    private static Task WriteCursorAsync(
        IStateStore state,
        int recordCount,
        HashSet<string> completedProjects,
        Dictionary<string, PerProjectStats> projectStats,
        string? inProgressKey = null,
        BatchContinuationToken? inProgressToken = null,
        int inProgressProcessedWorkItems = 0,
        int inProgressLinksFound = 0,
        int inProgressCrossProjectCount = 0,
        int inProgressCrossOrgCount = 0,
        CancellationToken ct = default)
    {
        object? inProgressObj = inProgressKey is not null
            ? new
            {
                key = inProgressKey,
                continuationToken = inProgressToken,
                processedWorkItems = inProgressProcessedWorkItems,
                linksFound = inProgressLinksFound,
                crossProjectCount = inProgressCrossProjectCount,
                crossOrgCount = inProgressCrossOrgCount
            }
            : null;

        var json = JsonSerializer.Serialize(new
        {
            recordCount,
            completedProjects = completedProjects.ToArray(),
            projectStats = projectStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    workItemsAnalysed = kvp.Value.WorkItemsAnalysed,
                    externalLinksFound = kvp.Value.ExternalLinksFound,
                    crossProjectCount = kvp.Value.CrossProjectCount,
                    crossOrgCount = kvp.Value.CrossOrgCount,
                    totalWorkItems = kvp.Value.TotalWorkItems
                }),
            inProgressProject = inProgressObj,
            savedAt = DateTime.UtcNow
        });
        return state.WriteAsync(CursorKey, json, ct);
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
        IArtefactStore store, string rootCsvContent, CancellationToken ct)
    {
        const string CsvHeader =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory";

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
        await store.WriteAsync(RootCsvPath, rootCsvContent, ct).ConfigureAwait(false);

        // Write per-org CSVs
        foreach (var kvp in perOrg)
        {
            await store.WriteAsync($"{kvp.Key}/dependencies.csv", kvp.Value.ToString(), ct).ConfigureAwait(false);
            _logger.LogInformation("Regenerated org-level dependencies CSV for {Org}.", kvp.Key);
        }

        // Write per-project CSVs
        foreach (var kvp in perProject)
        {
            await store.WriteAsync($"{kvp.Key}/dependencies.csv", kvp.Value.ToString(), ct).ConfigureAwait(false);
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
        IArtefactStore store, string rootCsvContent, CancellationToken ct)
    {
        // ── Step 1: Parse root CSV into grouped project-pair records ─────
        var pairAccumulator = new Dictionary<string, (string SourceProject, string TargetProject, string TargetOrganisation, LinkScope Scope, int Count)>(
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

            if (string.IsNullOrWhiteSpace(sourceProject)) continue;

            LinkScope scope;
            if (linkScopeStr.Equals("CrossOrganisation", StringComparison.OrdinalIgnoreCase))
                scope = LinkScope.CrossOrganisation;
            else
                scope = LinkScope.CrossProject;

            var pairKey = $"{sourceProject}|{targetProject}|{targetOrg}|{scope}";
            if (pairAccumulator.TryGetValue(pairKey, out var existing))
            {
                pairAccumulator[pairKey] = (existing.SourceProject, existing.TargetProject, existing.TargetOrganisation, existing.Scope, existing.Count + 1);
            }
            else
            {
                pairAccumulator[pairKey] = (sourceProject, targetProject, targetOrg, scope, 1);
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
            var (sourceProject, targetProject, targetOrg, scope, count) = kvp.Value;
            records.Add(new ProjectDependencyRecord
            {
                SourceProject = sourceProject,
                TargetProject = targetProject,
                TargetOrganisation = targetOrg,
                LinkCount = count,
                LinkScope = scope
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
        groupedCsv.AppendLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId");
        foreach (var rec in records)
        {
            groupedCsv.AppendLine(
                $"{EscapeCsv(rec.SourceProject)},{EscapeCsv(rec.TargetProject)},{EscapeCsv(rec.TargetOrganisation)}," +
                $"{rec.LinkCount},{rec.LinkScope},{rec.GroupId}");
        }
        await store.WriteAsync("discovery-project-dependencies.csv", groupedCsv.ToString(), ct).ConfigureAwait(false);
        _logger.LogInformation("Wrote discovery-project-dependencies.csv with {PairCount} project pairs.", records.Count);

        // ── Step 3: Write overall Mermaid diagram ────────────────────────
        var diagramBuilder = new MermaidDiagramBuilder(records);
        var mermaidContent = new StringBuilder();
        mermaidContent.AppendLine("# Project Dependency Graph");
        mermaidContent.AppendLine();
        mermaidContent.AppendLine("```mermaid");
        mermaidContent.Append(diagramBuilder.Build());
        mermaidContent.AppendLine("```");
        await store.WriteAsync("discovery-project-dependencies.md", mermaidContent.ToString(), ct).ConfigureAwait(false);
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

            await store.WriteAsync($"{key}/dependency-graph.md", projectMermaid.ToString(), ct).ConfigureAwait(false);
            _logger.LogDebug("Wrote transitive dependency graph for {Project}.", key);
        }

        _logger.LogInformation(
            "Generated analysis outputs: {PairCount} grouped pairs, {ProjectCount} per-project graphs.",
            records.Count, writtenProjects.Count);
    }

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

