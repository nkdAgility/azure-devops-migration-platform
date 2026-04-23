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
        var inventoryJson = await store.ReadAsync("inventory.json", ct).ConfigureAwait(false);
        if (inventoryJson is not null)
        {
            try
            {
                var report = JsonSerializer.Deserialize<InventoryReport>(inventoryJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                if (report is not null)
                {
                    grandTotalWorkItems = report.Totals.WorkItems;
                    _logger.LogInformation(
                        "Loaded inventory.json — {TotalWorkItems} total work items across {Projects} projects.",
                        report.Totals.WorkItems, report.Totals.Projects);

                    sink.Emit(new ProgressEvent
                    {
                        Module = Name,
                        Stage = "InventoryLoaded",
                        TotalWorkItems = (int)Math.Min(report.Totals.WorkItems, int.MaxValue),
                        Message = $"Inventory loaded: {report.Totals.WorkItems:N0} work items across {report.Totals.Projects} projects.",
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
                        WorkItemId = stats?.ExternalLinksFound ?? 0,
                        RevisionsProcessed = stats?.CrossProjectCount ?? 0,
                        AttachmentsProcessed = stats?.CrossOrgCount ?? 0,
                        Message = $"Resumed (previously completed)",
                        Timestamp = DateTimeOffset.UtcNow
                    });
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
                        WorkItemId = heartbeat.ExternalLinksFound,
                        RevisionsProcessed = heartbeat.CrossProjectCount,
                        AttachmentsProcessed = heartbeat.CrossOrgCount,
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
                        var completedKey = $"{heartbeat.OrganisationUrl}|{heartbeat.ProjectName}";
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

    private sealed record PerProjectStats(
        int WorkItemsAnalysed,
        int ExternalLinksFound,
        int CrossProjectCount,
        int CrossOrgCount,
        int TotalWorkItems);
}
