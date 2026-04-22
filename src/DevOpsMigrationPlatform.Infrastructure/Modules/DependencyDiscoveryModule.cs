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
    private const string CursorKey = "Checkpoints/Dependencies.cursor.json";
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
        var existingCsvRows = new StringBuilder();
        var recordCount = 0;

        var cursorJson = await state.ReadAsync(CursorKey, ct).ConfigureAwait(false);
        if (cursorJson is not null)
        {
            _logger.LogInformation("Found existing dependencies cursor — attempting resume.");

            // Parse completed project keys from cursor
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
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse dependencies cursor — starting fresh.");
                completedProjects.Clear();
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

        await foreach (var evt in dependencyService.DiscoverDependenciesAsync(completedProjects, null, ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case DependencyFoundEvent found:
                    var r = found.Record;
                    csvBuilder.AppendLine(
                        $"{r.SourceWorkItemId},{EscapeCsv(r.SourceWorkItemType ?? "")}," +
                        $"{EscapeCsv(r.SourceProject ?? "")},{EscapeCsv(r.SourceOrganisationUrl)}," +
                        $"{EscapeCsv(r.LinkType ?? "")},{r.LinkScope}," +
                        $"{r.TargetWorkItemId},{EscapeCsv(r.TargetProject ?? "")}," +
                        $"{EscapeCsv(r.TargetOrganisation ?? "")},{r.TargetStatus}," +
                        $"{(r.LinkChangedDate.HasValue ? r.LinkChangedDate.Value.ToString("O") : "")}," +
                        $"{EscapeCsv(r.SourceWorkItemStateCategory ?? "")}");
                    recordCount++;
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
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    // Flush CSV at checkpoint interval even mid-project so long-running
                    // projects produce visible output within minutes.
                    if (!heartbeat.IsComplete && DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
                    {
                        await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                        lastCheckpoint = DateTime.UtcNow;
                        _logger.LogDebug("Dependencies mid-project flush at checkpoint interval.");
                    }

                    if (heartbeat.IsComplete)
                    {
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogInformation(
                                "Completed project {Project} in {OrgUrl}: {Links} external links.",
                                heartbeat.ProjectName, heartbeat.OrganisationUrl, heartbeat.ExternalLinksFound);
                        // Organisation transition tracking.
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

                        // Flush CSV at every project boundary so results appear on disk immediately.
                        await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                    }
                    break;
            }

            // Flush CSV after every completed project so results are visible on disk immediately.
            // Checkpoint cursor at the configured interval for resume support.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                await WriteCursorAsync(state, recordCount, allCompletedProjects, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                metrics?.RecordCheckpointSaved(new TagList { { "job.id", job.JobId }, { "module", Name } });
                _logger.LogDebug("Dependencies checkpoint saved at {RecordCount} records, {CompletedProjects} projects completed.",
                    recordCount, allCompletedProjects.Count);
            }
        }

        // Complete final organisation.
        if (currentOrg is not null)
        {
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

        // Final write.
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

    private static Task WriteCursorAsync(IStateStore state, int recordCount, HashSet<string> completedProjects, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new
        {
            recordCount,
            completedProjects = completedProjects.ToArray(),
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
}
