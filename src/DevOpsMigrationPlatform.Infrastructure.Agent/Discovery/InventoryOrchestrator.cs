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
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// Orchestrates inventory data collection: consumes an <see cref="IAsyncEnumerable{InventoryProgressEvent}"/>
/// stream from <see cref="IInventoryService"/> and handles CSV/JSON writing, checkpointing,
/// progress events, metrics, and snapshots. Both <c>InventoryModule</c> (single-source export)
/// and <c>InventoryDiscoveryModule</c> (multi-org standalone) delegate to this orchestrator.
/// </summary>
internal sealed class InventoryOrchestrator : IInventoryOrchestrator
{
    /// <summary>
    /// Derives a module-scoped cursor key so that InventoryModule and InventoryDiscoveryModule
    /// can checkpoint independently when running in the same job.
    /// </summary>
    private static string CursorKeyFor(string moduleName) => PackagePaths.CursorFile(moduleName);

    private static string OrgCsvOutputPathFor(string orgSlug)
        => $"{orgSlug}/inventory.csv";

    private static string OrgJsonOutputPathFor(string orgSlug)
        => $"{orgSlug}/inventory.json";

    // Aggregate paths (all orgs combined).
    private static string CsvOutputPath => "inventory.csv";

    private static string JsonOutputPath => "inventory.json";

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _metrics;

    public InventoryOrchestrator(
        ILogger<InventoryOrchestrator> logger,
        IPlatformMetrics? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>
    /// Runs the inventory orchestration loop: consumes events, writes CSV/JSON,
    /// checkpoints, emits progress events and metrics.
    /// </summary>
    public async Task RunAsync(
        string moduleName,
        IAsyncEnumerable<InventoryProgressEvent> eventStream,
        InventoryContext context,
        int checkpointIntervalSeconds = 300,
        CancellationToken ct = default)
    {
        var job = context.Job;
        var store = context.ArtefactStore;
        var state = context.StateStore;
        var sink = context.ProgressSink ?? NullProgressSink.Instance;
        var metricsStore = context.MetricsStore;
        var snapshotStore = context.SnapshotStore;

        var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(context.SourceEndpoint?.ResolvedUrl ?? "unknown");
        var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? "unknown";
        var csvOutputPath = OrgCsvOutputPathFor(orgSlug);
        var jsonOutputPath = OrgJsonOutputPathFor(orgSlug);
        var aggregateJsonPath = JsonOutputPath;

        _logger.LogInformation("Inventory orchestrator starting for job {JobId}, module {Module}.", job.JobId, moduleName);

        // Read checkpoint — presence means a previous run was interrupted.
        var lastCompleted = await ReadCursorAsync(state, moduleName, ct).ConfigureAwait(false);
        var isResuming = lastCompleted is not null;

        if (isResuming)
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogInformation("Resuming inventory after project '{LastCompleted}'.", lastCompleted);

        // Emit a probe event so the CLI live table transitions from "…" to "Starting".
        sink.Emit(new ProgressEvent
        {
            Module = moduleName,
            Stage = "Progress",
            Message = "Inventory starting — connecting to source…",
            Timestamp = DateTimeOffset.UtcNow
        });

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,IsComplete,Error");

        var orgProjectData = new Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>>(StringComparer.OrdinalIgnoreCase);

        // Reload completed projects from existing CSV for resume support.
        if (isResuming)
        {
            var existingCsv = await store.ReadAsync(csvOutputPath, ct).ConfigureAwait(false);
            if (existingCsv is not null)
            {
                var lines = existingCsv.Split('\n');
                for (int i = 1; i < lines.Length; i++) // skip header
                {
                    var trimmed = lines[i].TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    csvBuilder.AppendLine(trimmed);
                    EmitCatchupFromCsvLine(sink, moduleName, trimmed);

                    var parts = trimmed.Split(',');
                    if (parts.Length >= 2)
                    {
                        var csvUrl = UnescapeCsv(parts[0]);
                        var csvProject = UnescapeCsv(parts[1]);

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
                    "Loaded {Count} previously-completed project(s) from existing CSV.",
                    orgProjectData.Values.Sum(l => l.Count));
            }

            sink.Emit(new ProgressEvent
            {
                Module = moduleName,
                Stage = "Resuming",
                Message = $"Resuming — skipping previously-completed project(s).",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        var checkpointInterval = TimeSpan.FromSeconds(checkpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;

        var metrics = _metrics;
        string? currentOrg = null;
        var jobSw = Stopwatch.StartNew();
        var orgSw = new Stopwatch();
        var projectSw = new Stopwatch();
        int orgProjectCount = 0;

        // All data within the processing loop references org URLs & project names — customer data.
        using var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);

        await foreach (var evt in eventStream.WithCancellation(ct).ConfigureAwait(false))
        {
            var projectKey = $"{evt.Url}|{evt.ProjectName}";

            // Forward intermediate heartbeats so the CLI live table updates progressively.
            if (!evt.IsComplete)
            {
                sink.Emit(new ProgressEvent
                {
                    Module = moduleName,
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

                // Flush CSV to disk at the checkpoint interval even mid-project.
                if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
                {
                    await store.WriteAsync(csvOutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                    await WriteInventoryJsonAsync(store, jsonOutputPath, orgProjectData, ct).ConfigureAwait(false);
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
                    var orgCompleteTags = new MetricsTagList
                    {
                        { "job.id", job.JobId },
                        { "module", moduleName },
                        { "organisation.url", currentOrg }
                    };
                    metrics?.SetProjectCount(orgProjectCount, orgCompleteTags);
                    metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, orgCompleteTags);
                    metrics?.OrganisationCompleted(orgCompleteTags);
                }
                currentOrg = evt.Url;
                orgProjectCount = 0;
                orgSw.Restart();
                var orgStartTags = new MetricsTagList
                {
                    { "job.id", job.JobId },
                    { "module", moduleName },
                    { "organisation.url", evt.Url }
                };
                metrics?.OrganisationStarted(orgStartTags);
            }
            projectSw.Restart();

            csvBuilder.AppendLine(
                $"{EscapeCsv(evt.Url)},{EscapeCsv(evt.ProjectName)},{evt.WorkItemsCount},{evt.RevisionsCount},{evt.ReposCount},{evt.IsComplete},{EscapeCsv(evt.Error ?? "")}");

            if (!orgProjectData.TryGetValue(evt.Url, out var orgList))
            {
                orgList = new List<(string, long, long, int, bool, string?)>();
                orgProjectData[evt.Url] = orgList;
            }
            orgList.Add((evt.ProjectName, evt.WorkItemsCount, evt.RevisionsCount, evt.ReposCount, evt.Error is null, evt.Error));

            // Write per-project inventory file: {orgSlug}/{project}/inventory.json
            var evtOrgSlug = PackagePathResolver.DeriveInventoryOrgSlug(evt.Url);
            var projectPath = PackagePathResolver.ProjectInventoryPath(evtOrgSlug, evt.ProjectName);
            await ProjectInventoryFile.MergeAsync(
                store, projectPath,
                orgUrl: evt.Url,
                project: evt.ProjectName,
                workItems: evt.WorkItemsCount,
                revisions: evt.RevisionsCount,
                repos: evt.ReposCount,
                isComplete: evt.Error is null,
                error: evt.Error,
                ct: ct).ConfigureAwait(false);

            projectSw.Stop();
            var projectTags = new MetricsTagList
            {
                { "job.id", job.JobId },
                { "module", moduleName },
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
                Module = moduleName,
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

            // Flush CSV and JSON after every completed project.
            await store.WriteAsync(csvOutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
            await WriteInventoryJsonAsync(store, jsonOutputPath, orgProjectData, ct).ConfigureAwait(false);

            var snapshots = BuildScopedOrganisations(orgProjectData, context.SourceEndpoint?.ResolvedUrl);
            PushAggregateMetrics(metricsStore, orgProjectData, snapshots);
            PushSnapshot(snapshotStore, orgProjectData, snapshots);

            // Checkpoint cursor at configured interval for resume support.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await WriteCursorAsync(state, moduleName, projectKey, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                metrics?.RecordCheckpointSaved(new MetricsTagList { { "job.id", job.JobId }, { "module", moduleName } });
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogDebug("Inventory checkpoint saved after project '{ProjectKey}'.", projectKey);
            }
        }

        // Complete final organisation.
        if (currentOrg is not null)
        {
            orgSw.Stop();
            var finalOrgTags = new MetricsTagList
            {
                { "job.id", job.JobId },
                { "module", moduleName },
                { "organisation.url", currentOrg }
            };
            metrics?.SetProjectCount(orgProjectCount, finalOrgTags);
            metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, finalOrgTags);
            metrics?.OrganisationCompleted(finalOrgTags);
        }

        // Final write — per-org CSV/JSON and rolling aggregate.
        await store.WriteAsync(csvOutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
        await WriteInventoryJsonAsync(store, jsonOutputPath, orgProjectData, ct).ConfigureAwait(false);
        await MergeAndWriteAggregateAsync(store, aggregateJsonPath, orgProjectData, ct).ConfigureAwait(false);

        await state.DeleteAsync(CursorKeyFor(moduleName), ct).ConfigureAwait(false);

        // Final snapshot push.
        var finalSnapshots = BuildScopedOrganisations(orgProjectData, context.SourceEndpoint?.ResolvedUrl);
        PushAggregateMetrics(metricsStore, orgProjectData, finalSnapshots);
        PushSnapshot(snapshotStore, orgProjectData, finalSnapshots);

        jobSw.Stop();
        metrics?.RecordJobDuration(jobSw.Elapsed.TotalMilliseconds, new MetricsTagList
        {
            { "job.id", job.JobId },
            { "module", moduleName }
        });

        sink.Emit(new ProgressEvent
        {
            Module = moduleName,
            Stage = "Completed",
            Message = "Inventory complete.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation("Inventory orchestrator completed for job {JobId}.", job.JobId);
    }

    /// <summary>
    /// Builds the set of completed project keys from the existing CSV so the
    /// service can skip them entirely — zero API calls for already-counted projects.
    /// </summary>
    public static async Task<HashSet<string>?> LoadCompletedKeysAsync(
        IArtefactStore store,
        IStateStore state,
        string moduleName,
        CancellationToken ct)
    {
        var lastCompleted = await ReadCursorAsync(state, moduleName, ct).ConfigureAwait(false);
        if (lastCompleted is null)
            return null;

        var completedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingCsv = await store.ReadAsync(CsvOutputPath, ct).ConfigureAwait(false);
        if (existingCsv is not null)
        {
            var lines = existingCsv.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                var parts = trimmed.Split(',');
                if (parts.Length >= 2)
                {
                    var csvUrl = UnescapeCsv(parts[0]);
                    var csvProject = UnescapeCsv(parts[1]);
                    completedKeys.Add($"{csvUrl}|{csvProject}");
                }
            }
        }

        return completedKeys.Count > 0 ? completedKeys : null;
    }

    /// <summary>
    /// Reads the existing aggregate inventory JSON (if any), removes the entry for the current
    /// org (to support retries), merges the current run's data, and writes the result.
    /// This ensures that a multi-org run accumulates all organisations in one file even though
    /// the orchestrator is called once per org.
    /// </summary>
    private static async Task MergeAndWriteAggregateAsync(
        IArtefactStore store,
        string aggregatePath,
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> currentOrgData,
        CancellationToken ct)
    {
        var mergedData = new Dictionary<string, List<(string, long, long, int, bool, string?)>>(StringComparer.OrdinalIgnoreCase);

        // Load previously-written orgs so they are preserved in the aggregate.
        var existingJson = await store.ReadAsync(aggregatePath, ct).ConfigureAwait(false);
        if (existingJson is not null)
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var existing = JsonSerializer.Deserialize<InventoryReport>(existingJson, opts);
                if (existing is not null)
                {
                    foreach (var org in existing.Organisations)
                    {
                        if (currentOrgData.ContainsKey(org.Url))
                            continue; // will be replaced by the current run's fresh data
                        mergedData[org.Url] = org.Projects.Select(p =>
                            (p.Name, p.WorkItems, p.Revisions, p.Repos, p.IsComplete, p.Error)).ToList();
                    }
                }
            }
            catch
            {
                // Corrupted aggregate — start fresh with just the current org.
            }
        }

        foreach (var kvp in currentOrgData)
            mergedData[kvp.Key] = kvp.Value;

        await WriteInventoryJsonAsync(store, aggregatePath, mergedData, ct).ConfigureAwait(false);
    }

    // ── Artefact writing helpers ──────────────────────────────────────────────

    private static async Task WriteInventoryJsonAsync(
        IArtefactStore store,
        string jsonOutputPath,
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
        await store.WriteAsync(jsonOutputPath, json, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<ScopedOrganisationEndpoint> BuildScopedOrganisations(
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> orgProjectData,
        string? sourceOrgUrl = null)
    {
        var result = orgProjectData.Select(kvp => new ScopedOrganisationEndpoint
        {
            Endpoint = new DerivedMigrationEndpointOptions(kvp.Key),
            Projects = kvp.Value.Select(p => p.Project).ToList()
        }).ToList();

        // Include org from source endpoint even if no projects were discovered.
        if (!string.IsNullOrWhiteSpace(sourceOrgUrl) &&
            !orgProjectData.ContainsKey(sourceOrgUrl!))
        {
            result.Add(new ScopedOrganisationEndpoint
            {
                Endpoint = new DerivedMigrationEndpointOptions(sourceOrgUrl!),
                Projects = new List<string>()
            });
        }

        return result;
    }

    private sealed class NullProgressSink : IProgressSink
    {
        public static readonly NullProgressSink Instance = new();
        public void Emit(ProgressEvent evt) { }
    }

    private sealed class DerivedMigrationEndpointOptions(string resolvedUrl) : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = resolvedUrl };
        public override string GetResolvedUrl() => resolvedUrl;
    }

    private static void PushAggregateMetrics(
        IJobMetricsStore? metricsStore,
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> orgProjectData,
        IReadOnlyList<ScopedOrganisationEndpoint> organisations)
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

        foreach (var org in organisations)
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

    private static void PushSnapshot(
        IJobSnapshotStore? snapshotStore,
        Dictionary<string, List<(string Project, long WorkItems, long Revisions, int Repos, bool IsComplete, string? Error)>> orgProjectData,
        IReadOnlyList<ScopedOrganisationEndpoint> organisations)
    {
        if (snapshotStore is null)
            return;

        var orgSnapshots = new List<OrgSnapshot>();
        foreach (var org in organisations)
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

    // ── Checkpoint helpers ────────────────────────────────────────────────────

    private static async Task<string?> ReadCursorAsync(IStateStore state, string moduleName, CancellationToken ct)
    {
        var raw = await state.ReadAsync(CursorKeyFor(moduleName), ct).ConfigureAwait(false);

        if (raw is null)
            raw = await state.ReadAsync(PackagePaths.Checkpoints + "/Inventory.cursor.json", ct).ConfigureAwait(false);

        if (raw is null)
            raw = await state.ReadAsync("Checkpoints/Inventory.cursor.json", ct).ConfigureAwait(false);

        if (raw is null) return null;
        var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("lastCompleted", out var el) ? el.GetString() : null;
    }

    private static Task WriteCursorAsync(IStateStore state, string moduleName, string lastCompleted, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { lastCompleted });
        return state.WriteAsync(CursorKeyFor(moduleName), json, ct);
    }

    // ── CSV helpers ──────────────────────────────────────────────────────────

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string UnescapeCsv(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
        return value;
    }

    private static void EmitCatchupFromCsvLine(IProgressSink sink, string moduleName, string csvLine)
    {
        var parts = csvLine.Split(',');
        if (parts.Length < 6) return;

        var url = UnescapeCsv(parts[0]);
        var projectName = UnescapeCsv(parts[1]);
        if (!int.TryParse(parts[2], out var workItems)) return;
        if (!int.TryParse(parts[3], out var revisions)) return;
        if (!int.TryParse(parts[4], out var repos)) return;

        sink.Emit(new ProgressEvent
        {
            Module = moduleName,
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
}

