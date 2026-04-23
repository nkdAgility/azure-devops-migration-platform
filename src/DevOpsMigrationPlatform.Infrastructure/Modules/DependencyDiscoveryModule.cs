using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Utilities;
using DevOpsMigrationPlatform.Infrastructure.Modules.Discovery;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Discovery module that analyses cross-project and cross-organisation work item links.
/// Wraps <see cref="IDependencyDiscoveryService"/> and writes <c>dependencies.csv</c>
/// to the artefact store. Checkpoints periodically so multi-hour runs can resume without
/// reprocessing already-analysed work items.
/// </summary>
public sealed class DependencyDiscoveryModule : IDiscoveryModule
{
    private const string CursorKey = PackagePaths.Checkpoints + "/Dependencies.cursor.json";
    private const string RootCsvPath = "dependencies.csv";

    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly ILogger<DependencyDiscoveryModule> _logger;
    private readonly IDiscoveryMetrics? _metrics;

    public string Name => "Dependencies";
    public DiscoveryJobType DiscoveryType => DiscoveryJobType.Dependencies;

    /// <summary>
    /// Normalizes an organisation URL + project name pair into a canonical key
    /// by trimming trailing slashes and lower-casing both parts.
    /// </summary>
    private static string NormalizeProjectKey(string orgUrl, string project)
        => $"{orgUrl.TrimEnd('/').ToLowerInvariant()}|{project.Trim().ToLowerInvariant()}";

    public DependencyDiscoveryModule(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        ILogger<DependencyDiscoveryModule> logger
        , IDiscoveryMetrics? metrics = null
        )
    {
        _dependencyFactory = dependencyFactory;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task RunAsync(DiscoveryContext context, CancellationToken ct)
    {
        var job = context.Job;
        var store = context.ArtefactStore;
        var state = context.StateStore;
        var sink = context.ProgressSink;

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
                        Module = Name,
                        Stage = "InventoryLoaded",
                        TotalWorkItems = (int)Math.Min(inventoryReport.Totals.WorkItems, int.MaxValue),
                        Message = $"Inventory loaded: {inventoryReport.Totals.WorkItems:N0} work items across {inventoryReport.Totals.Projects} projects.",
                        Timestamp = DateTimeOffset.UtcNow
                    });
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

        var dependencyService = _dependencyFactory.Create(job.Organisations, job.Policies);

        // ── Resume: read existing cursor and CSV ─────────────────────────────
        var completedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resumedProjectStats = new Dictionary<string, PerProjectStats>(StringComparer.OrdinalIgnoreCase);
        var existingCsvRows = new StringBuilder();
        var recordCount = 0;

        var cursorJson = await state.ReadAsync(CursorKey, ct).ConfigureAwait(false);

        // Legacy fallback: try the pre-.migration path for existing packages.
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
                if (doc.RootElement.TryGetProperty("projectStats", out var statsObj))
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
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse dependencies cursor — starting fresh.");
                completedProjects.Clear();
                resumedProjectStats.Clear();
                recordCount = 0;
            }

            // Reload existing CSV so we append rather than overwrite
            if (completedProjects.Count > 0)
            {
                var existingCsv = await store.ReadAsync(RootCsvPath, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(existingCsv))
                {
                    existingCsvRows.Append(existingCsv);
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

                    resumedProjectStats.TryGetValue(projectKey, out var stats);
                    sink.Emit(new ProgressEvent
                    {
                        Module = Name,
                        Stage = "ProjectComplete",
                        LastProcessed = projectKey,
                        TotalWorkItems = stats?.TotalWorkItems ?? 0,
                        WorkItemsProcessed = stats?.WorkItemsAnalysed ?? 0,
                        ExternalLinksFound = stats?.ExternalLinksFound ?? 0,
                        CrossProjectLinks = stats?.CrossProjectCount ?? 0,
                        CrossOrgLinks = stats?.CrossOrgCount ?? 0,
                        Message = $"Resumed (previously completed)",
                        Timestamp = DateTimeOffset.UtcNow
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
                    await WriteCursorAsync(state, recordCount, completedProjects, reconciledStats, ct).ConfigureAwait(false);

                    _logger.LogWarning(
                        "Reconciled dependencies cursor from CSV — {CompletedCount} project(s), {RecordCount} records. " +
                        "Projects already analysed will be skipped on this run.",
                        completedProjects.Count, recordCount);

                    sink.Emit(new ProgressEvent
                    {
                        Module = Name,
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
                            Module = Name,
                            Stage = "ProjectComplete",
                            LastProcessed = displayKey,
                            TotalWorkItems = stats?.TotalWorkItems ?? 0,
                            WorkItemsProcessed = stats?.WorkItemsAnalysed ?? 0,
                            ExternalLinksFound = stats?.ExternalLinksFound ?? 0,
                            CrossProjectLinks = stats?.CrossProjectCount ?? 0,
                            CrossOrgLinks = stats?.CrossOrgCount ?? 0,
                            Message = $"Reconciled from CSV",
                            Timestamp = DateTimeOffset.UtcNow
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
            foreach (var org in job.Organisations)
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
                        Module = Name,
                        Stage = "ProjectComplete",
                        LastProcessed = displayKey,
                        TotalWorkItems = stats.TotalWorkItems,
                        WorkItemsProcessed = stats.WorkItemsAnalysed,
                        ExternalLinksFound = stats.ExternalLinksFound,
                        CrossProjectLinks = stats.CrossProjectCount,
                        CrossOrgLinks = stats.CrossOrgCount,
                        Message = $"{displayKey}: {stats.WorkItemsAnalysed}/{stats.TotalWorkItems} analysed, {stats.ExternalLinksFound} links found (reconciled)",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                // Clean up the cursor — analysis is fully complete
                await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

                sink.Emit(new ProgressEvent
                {
                    Module = Name,
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

        var checkpointInterval = TimeSpan.FromSeconds(job.Policies.CheckpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;

        var metrics = _metrics;
        string? currentOrg = null;
        var orgSw = new Stopwatch();
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

        await foreach (var evt in dependencyService.DiscoverDependenciesAsync(completedProjects, null, ct).ConfigureAwait(false))
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
                    var recOrgFolder = PathUtilities.ExtractOrgFolderName(r.SourceOrganisationUrl);
                    var recProjectFolder = $"{recOrgFolder}/{PathUtilities.Sanitise(r.SourceProject ?? "unknown")}";
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

                    metrics?.RecordLinksFound(1, new TagList
                    {
                        { "job.id", job.JobId },
                        { "module", Name },
                        { "organisation.url", r.SourceOrganisationUrl },
                        { "link.scope", r.LinkScope.ToString() }
                    });
                    break;

                case DependencyHeartbeatEvent heartbeat:
                    sink.Emit(new ProgressEvent
                    {
                        Module = Name,
                        Stage = heartbeat.IsComplete ? "ProjectComplete" : "Analysis",
                        LastProcessed = $"{heartbeat.OrganisationUrl}|{heartbeat.ProjectName}",
                        TotalWorkItems = heartbeat.TotalWorkItems,
                        WorkItemsProcessed = heartbeat.WorkItemsAnalysed,
                        ExternalLinksFound = heartbeat.ExternalLinksFound,
                        CrossProjectLinks = heartbeat.CrossProjectCount,
                        CrossOrgLinks = heartbeat.CrossOrgCount,
                        Message = heartbeat.Error is not null
                            ? $"{heartbeat.OrganisationUrl}/{heartbeat.ProjectName}: failed — {heartbeat.Error}"
                            : $"{heartbeat.OrganisationUrl}/{heartbeat.ProjectName}: " +
                              $"{heartbeat.WorkItemsAnalysed}/{heartbeat.TotalWorkItems} analysed, {heartbeat.ExternalLinksFound} links found",
                        Timestamp = DateTimeOffset.UtcNow,
                        LastCheckpointAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero),
                        NextCheckpointDueAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero) + checkpointInterval
                    });

                    // Flush CSV at checkpoint interval even mid-project so long-running
                    // projects produce visible output within minutes.
                    if (!heartbeat.IsComplete && DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
                    {
                        await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);

                        // Also flush the current project's partial CSV.
                        var midOrgFolder = PathUtilities.ExtractOrgFolderName(heartbeat.OrganisationUrl);
                        var midProjectFolder = $"{midOrgFolder}/{PathUtilities.Sanitise(heartbeat.ProjectName)}";
                        if (perProjectCsv.TryGetValue(midProjectFolder, out var midProjCsv))
                            await store.WriteAsync($"{midProjectFolder}/dependencies.csv", midProjCsv.ToString(), ct).ConfigureAwait(false);

                        // Also flush the current org's partial CSV.
                        if (currentOrgFolder is not null && currentOrgCsvBuilder.Length > 0)
                            await store.WriteAsync($"{currentOrgFolder}/dependencies.csv", currentOrgCsvBuilder.ToString(), ct).ConfigureAwait(false);

                        lastCheckpoint = DateTime.UtcNow;
                        _logger.LogDebug("Dependencies mid-project flush at checkpoint interval.");
                    }

                    if (heartbeat.IsComplete)
                    {
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogInformation(
                                "Completed project {Project} in {OrgUrl}: {Links} external links.",
                                heartbeat.ProjectName, heartbeat.OrganisationUrl, heartbeat.ExternalLinksFound);

                        var hbOrgFolder = PathUtilities.ExtractOrgFolderName(heartbeat.OrganisationUrl);
                        var hbProjectFolder = $"{hbOrgFolder}/{PathUtilities.Sanitise(heartbeat.ProjectName)}";

                        // Organisation transition tracking (metrics only — CSV accumulator
                        // transitions are handled by the DependencyFoundEvent handler).
                        if (currentOrg != heartbeat.OrganisationUrl)
                        {
                            if (currentOrg is not null)
                            {
                                orgSw.Stop();
                                var orgCompleteTags = new TagList
                                {
                                    { "job.id", job.JobId },
                                    { "module", Name },
                                    { "organisation.url", currentOrg }
                                };
                                metrics?.SetProjectCount(orgProjectCount, orgCompleteTags);
                                metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, orgCompleteTags);
                                metrics?.OrganisationCompleted(orgCompleteTags);
                            }
                            currentOrg = heartbeat.OrganisationUrl;
                            orgProjectCount = 0;
                            orgSw.Restart();
                            metrics?.OrganisationStarted(new TagList
                            {
                                { "job.id", job.JobId },
                                { "module", Name },
                                { "organisation.url", heartbeat.OrganisationUrl }
                            });
                        }

                        // Update current project folder for DependencyFoundEvent tracking.
                        currentProjectFolder = hbProjectFolder;
                        if (currentOrgFolder is null)
                            currentOrgFolder = hbOrgFolder;

                        var projectTags = new TagList
                        {
                            { "job.id", job.JobId },
                            { "module", Name },
                            { "organisation.url", heartbeat.OrganisationUrl },
                            { "project.name", heartbeat.ProjectName }
                        };
                        metrics?.ProjectStarted(projectTags);
                        metrics?.ProjectCompleted(projectTags);
                        metrics?.RecordWorkItemsAnalysed(heartbeat.WorkItemsAnalysed, projectTags);
                        orgProjectCount++;

                        // Track this project as completed for the cursor
                        var completedKey = NormalizeProjectKey(heartbeat.OrganisationUrl, heartbeat.ProjectName);
                        allCompletedProjects.Add(completedKey);

                        // Save per-project stats so they are available when a resumed job
                        // emits synthetic ProjectComplete events for the CLI live table.
                        allProjectStats[completedKey] = new PerProjectStats(
                            WorkItemsAnalysed: heartbeat.WorkItemsAnalysed,
                            ExternalLinksFound: heartbeat.ExternalLinksFound,
                            CrossProjectCount: heartbeat.CrossProjectCount,
                            CrossOrgCount: heartbeat.CrossOrgCount,
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
                await WriteCursorAsync(state, recordCount, allCompletedProjects, allProjectStats, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                metrics?.RecordCheckpointSaved(new TagList { { "job.id", job.JobId }, { "module", Name } });
                _logger.LogDebug("Dependencies checkpoint saved at {RecordCount} records, {CompletedProjects} projects completed.",
                    recordCount, allCompletedProjects.Count);
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
            var finalOrgTags = new TagList
            {
                { "job.id", job.JobId },
                { "module", Name },
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
        foreach (var org in job.Organisations)
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
                        Module = Name,
                        Stage = "ProjectComplete",
                        LastProcessed = $"{resolvedUrl}|{project}",
                        TotalWorkItems = totalWi,
                        WorkItemsProcessed = totalWi,
                        ExternalLinksFound = 0,
                        CrossProjectLinks = 0,
                        CrossOrgLinks = 0,
                        Message = $"{resolvedUrl}/{project}: {totalWi} analysed, 0 external links",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }
        }

        await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

        sink.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Completed",
            Message = $"Dependency analysis complete. {recordCount} external links written.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation(
            "Dependencies module completed for job {JobId}. {RecordCount} records written.",
            job.JobId, recordCount);
    }

    private static Task WriteCursorAsync(IStateStore state, int recordCount, HashSet<string> completedProjects, Dictionary<string, PerProjectStats> projectStats, CancellationToken ct)
    {
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
            savedAt = DateTime.UtcNow
        });
        return state.WriteAsync(CursorKey, json, ct);
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
    /// Used during reconciliation to emit accurate <c>ProjectComplete</c> progress events.
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
    /// Called when reconciliation detects all projects are already complete but output files
    /// may be missing (e.g. the cursor was lost after the root CSV was written but before
    /// per-org/per-project files were flushed).
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

            var orgFolder = PathUtilities.ExtractOrgFolderName(sourceOrgUrl);
            var projectFolder = $"{orgFolder}/{PathUtilities.Sanitise(sourceProject)}";
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
    /// Generates the analysis outputs from the root <c>dependencies.csv</c>:
    /// <list type="bullet">
    ///   <item><c>discovery-project-dependencies.csv</c> — grouped project-pair summary</item>
    ///   <item><c>discovery-project-dependencies.md</c> — overall Mermaid dependency diagram</item>
    ///   <item><c>{orgFolder}/{project}/dependency-graph.md</c> — per-project transitive Mermaid diagram</item>
    /// </list>
    /// Called at the end of both normal completion and reconciled early-exit paths.
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
                return PathUtilities.ExtractOrgFolderName(cols[3]);
            }
        }
        return null;
    }

    /// <summary>
    /// Parses a single CSV line respecting quoted fields (RFC 4180).
    /// Used by reconciliation to extract project keys from existing CSV data.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
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

    private sealed record PerProjectStats(
        int WorkItemsAnalysed,
        int ExternalLinksFound,
        int CrossProjectCount,
        int CrossOrgCount,
        int TotalWorkItems);
}
