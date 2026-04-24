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
/// Discovery module that counts work items and revisions per project across all configured
/// organisations. Wraps <see cref="IInventoryService"/> and writes <c>inventory.csv</c>
/// and <c>inventory.json</c> to the artefact store. Checkpoints after each project so
/// a 20+ hour run can resume. The JSON report is consumed by the dependency analysis
/// pass to obtain grand totals before link analysis begins.
/// <para>
/// <strong>Architecture note:</strong> This module follows the delegation pattern: it orchestrates
/// checkpointing, progress reporting, and artefact writing, while the actual Azure DevOps API
/// interaction is delegated to <see cref="IInventoryService"/> (created via factory). This separation
/// keeps the module testable with mocked services and decoupled from any specific connector.
/// </para>
/// </summary>
public sealed class InventoryDiscoveryModule : IDiscoveryModule
{
    private static readonly string CursorKey = PackagePaths.CursorFile("Inventory");
    private const string CsvOutputPath = "inventory.csv";
    private const string JsonOutputPath = "inventory.json";

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly IInventoryServiceFactory _inventoryFactory;
    private readonly ILogger<InventoryDiscoveryModule> _logger;
    private readonly IDiscoveryMetrics? _metrics;

    public string Name => "Inventory";
    public DiscoveryJobType DiscoveryType => DiscoveryJobType.Inventory;

    public InventoryDiscoveryModule(
        IInventoryServiceFactory inventoryFactory,
        ILogger<InventoryDiscoveryModule> logger
        , IDiscoveryMetrics? metrics = null
        )
    {
        _inventoryFactory = inventoryFactory;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task RunAsync(DiscoveryContext context, CancellationToken ct)
    {
        using var rootActivity = ActivitySource.StartActivity("discovery.inventory", ActivityKind.Internal);
        rootActivity?.SetTag("job.id", context.Job.JobId);

        var job = context.Job;
        var store = context.ArtefactStore;
        var state = context.StateStore;
        var sink = context.ProgressSink;
        var metricsStore = context.MetricsStore;
        var snapshotStore = context.SnapshotStore;

        _logger.LogInformation("Inventory module starting for job {JobId}.", job.JobId);

        // Read checkpoint — presence means a previous run was interrupted.
        var lastCompleted = await ReadCursorAsync(state, ct).ConfigureAwait(false);
        var isResuming = lastCompleted is not null;

        if (isResuming)
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogInformation("Resuming inventory after project '{LastCompleted}'.", lastCompleted);

        var inventoryService = _inventoryFactory.Create(job.Organisations, job.Policies);

        // Emit a probe event so the CLI live table transitions from "…" to "Starting"
        // immediately, proving the sink pipeline works before the API returns data.
        sink.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Progress",
            Message = "Inventory starting — connecting to source…",
            Timestamp = DateTimeOffset.UtcNow
        });

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,IsComplete,Error");

        // Collect per-org/per-project data for the inventory.json report.
        var orgProjectData = new Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>>(StringComparer.OrdinalIgnoreCase);

        // Build the set of completed project keys from the existing CSV so the
        // service can skip them entirely — zero API calls for already-counted projects.
        var completedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (isResuming)
        {
            var existingCsv = await store.ReadAsync(CsvOutputPath, ct).ConfigureAwait(false);
            if (existingCsv is not null)
            {
                var lines = existingCsv.Split('\n');
                for (int i = 1; i < lines.Length; i++) // skip header
                {
                    var trimmed = lines[i].TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    csvBuilder.AppendLine(trimmed);
                    EmitCatchupFromCsvLine(sink, trimmed);

                    var parts = trimmed.Split(',');
                    if (parts.Length >= 2)
                    {
                        var csvUrl = UnescapeCsv(parts[0]);
                        var csvProject = UnescapeCsv(parts[1]);
                        completedKeys.Add($"{csvUrl}|{csvProject}");

                        // Populate orgProjectData from resumed CSV rows.
                        if (parts.Length >= 5)
                        {
                            int.TryParse(parts[2], out var wi);
                            int.TryParse(parts[3], out var rev);
                            int.TryParse(parts[4], out var repos);
                            if (!orgProjectData.TryGetValue(csvUrl, out var list))
                            {
                                list = new List<(string, long, long, int, bool, string?)>();
                                orgProjectData[csvUrl] = list;
                            }
                            list.Add((csvProject, wi, rev, repos, true, null));
                        }
                    }
                }
                _logger.LogInformation(
                    "Loaded {Count} previously-completed project(s) from existing CSV.", completedKeys.Count);
            }

            sink.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Resuming",
                Message = $"Resuming — skipping {completedKeys.Count} previously-completed project(s).",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        var checkpointInterval = TimeSpan.FromSeconds(job.Policies.CheckpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;

        var metrics = _metrics;
        string? currentOrg = null;
        var jobSw = Stopwatch.StartNew();
        var orgSw = new Stopwatch();
        var projectSw = new Stopwatch();
        int orgProjectCount = 0;

        // All data within the processing loop references org URLs & project names — customer data.
        using var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);

        // Pass completed keys so the service skips them — no re-counting.
        await foreach (var evt in inventoryService.RunInventoryAsync(
            completedKeys.Count > 0 ? completedKeys : null, ct).ConfigureAwait(false))
        {
            var projectKey = $"{evt.Url}|{evt.ProjectName}";

            // Forward intermediate heartbeats so the CLI live table updates progressively
            // (e.g. Petrel: 291k work items counted ~200 at a time).
            if (!evt.IsComplete)
            {
                sink.Emit(new ProgressEvent
                {
                    Module = Name,
                    Stage = "Progress",
                    Message = $"{evt.Url}|{evt.ProjectName}",
                    Timestamp = DateTimeOffset.UtcNow,
                    LastCheckpointAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero),
                    NextCheckpointDueAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero) + checkpointInterval,
                    Metrics = new JobMetrics
                    {
                        Scope = new JobScopeCounters { WorkItemsTotal = evt.WorkItemsCount },
                        Discovery = new DiscoveryCounters
                        {
                            Inventory = new InventoryCounters
                            {
                                RevisionsTotal = 0,
                                RepositoriesTotal = 0
                            }
                        }
                    }
                });

                // Flush CSV to disk at the checkpoint interval even mid-project so
                // long-running projects (e.g. Petrel) produce visible output within minutes.
                if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
                {
                    await store.WriteAsync(CsvOutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                    await WriteInventoryJsonAsync(store, orgProjectData, ct).ConfigureAwait(false);
                    lastCheckpoint = DateTime.UtcNow;
                    _logger.LogDebug("Inventory mid-project flush at checkpoint interval.");
                }

                continue;
            }

            // Organisation transition tracking.
            if (currentOrg != evt.Url)
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
                currentOrg = evt.Url;
                orgProjectCount = 0;
                orgSw.Restart();
                var orgStartTags = new TagList
                {
                    { "job.id", job.JobId },
                    { "module", Name },
                    { "organisation.url", evt.Url }
                };
                metrics?.OrganisationStarted(orgStartTags);
            }
            projectSw.Restart();

            csvBuilder.AppendLine(
                $"{EscapeCsv(evt.Url)},{EscapeCsv(evt.ProjectName)},{evt.WorkItemsCount},{evt.RevisionsCount},{evt.ReposCount},{evt.IsComplete},{EscapeCsv(evt.Error ?? "")}");

            // Collect per-project data for inventory.json.
            if (!orgProjectData.TryGetValue(evt.Url, out var orgList))
            {
                orgList = new List<(string, long, long, int, bool, string?)>();
                orgProjectData[evt.Url] = orgList;
            }
            orgList.Add((evt.ProjectName, evt.WorkItemsCount, evt.RevisionsCount, evt.ReposCount, evt.Error is null, evt.Error));

            projectSw.Stop();
            var projectTags = new TagList
            {
                { "job.id", job.JobId },
                { "module", Name },
                { "organisation.url", evt.Url },
                { "project.name", evt.ProjectName }
            };
            metrics?.ProjectStarted(projectTags);
            if (evt.Error is not null)
            {
                metrics?.ProjectFailed(projectTags);
            }
            else
            {
                metrics?.ProjectCompleted(projectTags);
                metrics?.RecordWorkItemsCounted(evt.WorkItemsCount, projectTags);
                metrics?.RecordRevisionsCounted(evt.RevisionsCount, projectTags);
                metrics?.RecordReposCounted(evt.ReposCount, projectTags);
            }
            metrics?.RecordProjectDuration(projectSw.Elapsed.TotalMilliseconds, projectTags);
            orgProjectCount++;

            sink.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = evt.Error is not null ? "Failed" : "Inventory",
                Message = $"{evt.Url}|{evt.ProjectName}",
                Timestamp = DateTimeOffset.UtcNow,
                LastCheckpointAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero),
                NextCheckpointDueAt = new DateTimeOffset(lastCheckpoint, TimeSpan.Zero) + checkpointInterval,
                Metrics = new JobMetrics
                {
                    Scope = new JobScopeCounters { WorkItemsTotal = evt.WorkItemsCount },
                    Discovery = new DiscoveryCounters
                    {
                        Inventory = new InventoryCounters
                        {
                            RevisionsTotal = evt.RevisionsCount,
                            RepositoriesTotal = evt.ReposCount
                        }
                    }
                }
            });

            // Flush CSV and JSON after every completed project so results are
            // visible on disk immediately — not only at checkpoint intervals.
            await store.WriteAsync(CsvOutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
            await WriteInventoryJsonAsync(store, orgProjectData, ct).ConfigureAwait(false);

            // Push aggregate metrics to the snapshot store (Channel 2) so the
            // TUI/CLI can read them via GET /jobs/{id}/telemetry.
            PushAggregateMetrics(metricsStore, orgProjectData, job);
            PushSnapshot(snapshotStore, orgProjectData, job);

            // Checkpoint cursor at configured interval for resume support.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await WriteCursorAsync(state, projectKey, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                metrics?.RecordCheckpointSaved(new TagList { { "job.id", job.JobId }, { "module", Name } });
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogDebug("Inventory checkpoint saved after project '{ProjectKey}'.", projectKey);
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

        // Final write — CSV and JSON.
        await store.WriteAsync(CsvOutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
        await WriteInventoryJsonAsync(store, orgProjectData, ct).ConfigureAwait(false);
        await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

        // Final snapshot push
        PushAggregateMetrics(metricsStore, orgProjectData, job);
        PushSnapshot(snapshotStore, orgProjectData, job);

        jobSw.Stop();
        metrics?.RecordJobDuration(jobSw.Elapsed.TotalMilliseconds, new TagList
        {
            { "job.id", job.JobId },
            { "module", Name }
        });

        sink.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Completed",
            Message = "Inventory complete.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation("Inventory module completed for job {JobId}.", job.JobId);
    }

    /// <summary>
    /// Builds an <see cref="InventoryReport"/> from the collected per-org/per-project data
    /// and writes it to the artefact store as <c>inventory.json</c>.
    /// </summary>
    private static async Task WriteInventoryJsonAsync(
        IArtefactStore store,
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> orgProjectData,
        CancellationToken ct)
    {
        var organisations = orgProjectData.Select(kvp =>
        {
            var projects = kvp.Value.Select(p => new ProjectInventory
            {
                Name = p.Project,
                WorkItems = p.WorkItems,
                Revisions = p.Revisions,
                Repos = p.Repos,
                IsComplete = p.IsComplete,
                Error = p.Error
            }).ToList();

            return new OrganisationInventory
            {
                Url = kvp.Key,
                Totals = new InventoryTotals
                {
                    WorkItems = projects.Sum(p => p.WorkItems),
                    Revisions = projects.Sum(p => p.Revisions),
                    Repos = projects.Sum(p => p.Repos),
                    Projects = projects.Count
                },
                Projects = projects
            };
        }).ToList();

        var report = new InventoryReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Totals = new InventoryTotals
            {
                WorkItems = organisations.Sum(o => o.Totals.WorkItems),
                Revisions = organisations.Sum(o => o.Totals.Revisions),
                Repos = organisations.Sum(o => o.Totals.Repos),
                Projects = organisations.Sum(o => o.Totals.Projects)
            },
            Organisations = organisations
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await store.WriteAsync(JsonOutputPath, json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes aggregate <see cref="JobMetrics"/> to the snapshot store (Channel 2)
    /// so the Control Plane telemetry endpoint reflects discovery progress.
    /// </summary>
    private static void PushAggregateMetrics(
        IJobMetricsStore? metricsStore,
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> orgProjectData,
        DiscoveryJob job)
    {
        if (metricsStore is null)
            return;

        int orgsTotal = orgProjectData.Count;
        int projectsTotal = 0, projectsCompleted = 0, projectsFailed = 0;
        long totalWi = 0, totalRev = 0;
        int totalRepos = 0;
        int orgsCompleted = 0, orgsFailed = 0;

        foreach (var kvp in orgProjectData)
        {
            var projects = kvp.Value;
            bool allDone = true;
            bool anyFailed = false;
            foreach (var p in projects)
            {
                projectsTotal++;
                if (p.Error is not null) { projectsFailed++; anyFailed = true; }
                else if (p.IsComplete) projectsCompleted++;
                else allDone = false;
                totalWi += p.WorkItems;
                totalRev += p.Revisions;
                totalRepos += p.Repos;
            }
            if (allDone && projects.Count > 0)
            {
                if (anyFailed) orgsFailed++; else orgsCompleted++;
            }
        }

        // Include configured orgs/projects that haven't started yet
        foreach (var org in job.Organisations)
        {
            var url = org.Endpoint.GetResolvedUrl();
            if (!orgProjectData.ContainsKey(url))
                orgsTotal++;
        }

        metricsStore.Update(new JobMetrics
        {
            Timestamp = DateTimeOffset.UtcNow,
            Scope = new JobScopeCounters
            {
                OrganisationsTotal = orgsTotal,
                OrganisationsCompleted = orgsCompleted,
                OrganisationsFailed = orgsFailed,
                ProjectsTotal = projectsTotal,
                ProjectsCompleted = projectsCompleted,
                ProjectsFailed = projectsFailed,
                WorkItemsTotal = totalWi
            },
            Discovery = new DiscoveryCounters
            {
                Inventory = new InventoryCounters
                {
                    RevisionsTotal = totalRev,
                    RepositoriesTotal = totalRepos
                }
            }
        });
    }

    /// <summary>
    /// Pushes a <see cref="JobSnapshot"/> (Channel 3) with per-org/project inventory state.
    /// </summary>
    private static void PushSnapshot(
        IJobSnapshotStore? snapshotStore,
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> orgProjectData,
        DiscoveryJob job)
    {
        if (snapshotStore is null)
            return;

        var orgSnapshots = new List<OrgSnapshot>();
        foreach (var org in job.Organisations)
        {
            var url = org.Endpoint.GetResolvedUrl();
            var projectSnapshots = new List<ProjectSnapshot>();

            if (orgProjectData.TryGetValue(url, out var projects))
            {
                foreach (var p in projects)
                {
                    projectSnapshots.Add(new ProjectSnapshot
                    {
                        Name = p.Project,
                        Status = p.Error is not null ? ProjectStatus.Failed
                               : p.IsComplete ? ProjectStatus.Completed
                               : ProjectStatus.InProgress,
                        Discovery = new DiscoveryCounters
                        {
                            Inventory = new InventoryCounters
                            {
                                RevisionsTotal = p.Revisions,
                                RepositoriesTotal = p.Repos
                            }
                        }
                    });
                }
            }

            orgSnapshots.Add(new OrgSnapshot
            {
                Url = url,
                Name = org.Endpoint.GetResolvedUrl(),
                Projects = projectSnapshots
            });
        }

        snapshotStore.Update(new JobSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Organisations = orgSnapshots
        });
    }

    private static async Task<string?> ReadCursorAsync(IStateStore state, CancellationToken ct)
    {
        var raw = await state.ReadAsync(CursorKey, ct).ConfigureAwait(false);

        // Legacy fallback: capitalised filename in .migration/Checkpoints/ (pre-standardisation).
        if (raw is null)
            raw = await state.ReadAsync(PackagePaths.Checkpoints + "/Inventory.cursor.json", ct).ConfigureAwait(false);

        // Legacy fallback: pre-.migration path for old packages.
        if (raw is null)
            raw = await state.ReadAsync("Checkpoints/Inventory.cursor.json", ct).ConfigureAwait(false);

        if (raw is null) return null;
        var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("lastCompleted", out var el) ? el.GetString() : null;
    }

    private static Task WriteCursorAsync(IStateStore state, string lastCompleted, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { lastCompleted });
        return state.WriteAsync(CursorKey, json, ct);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>
    /// Emits a catchup <see cref="ProgressEvent"/> from a CSV data line so the CLI live table
    /// shows actual counts for projects completed in a previous (interrupted) run.
    /// </summary>
    private static void EmitCatchupFromCsvLine(IProgressSink sink, string csvLine)
    {
        // CSV format: Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,IsComplete,Error
        var parts = csvLine.Split(',');
        if (parts.Length < 6) return;

        var url = UnescapeCsv(parts[0]);
        var projectName = UnescapeCsv(parts[1]);
        if (!int.TryParse(parts[2], out var workItems)) return;
        if (!int.TryParse(parts[3], out var revisions)) return;
        if (!int.TryParse(parts[4], out var repos)) return;

        sink.Emit(new ProgressEvent
        {
            Module = "Inventory",
            Stage = "Inventory",
            Message = $"{url}|{projectName}",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Scope = new JobScopeCounters { WorkItemsTotal = workItems },
                Discovery = new DiscoveryCounters
                {
                    Inventory = new InventoryCounters
                    {
                        RevisionsTotal = revisions,
                        RepositoriesTotal = repos
                    }
                }
            }
        });
    }

    private static string UnescapeCsv(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
        return value;
    }
}
