// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
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
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
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
    private static string OrgCsvOutputPathFor(string orgSlug)
        => $"{orgSlug}/inventory.csv";

    private static string OrgJsonOutputPathFor(string orgSlug)
        => $"{orgSlug}/inventory.json";

    // Aggregate paths (all orgs combined).
    private static string CsvOutputPath => "inventory.csv";

    private static string JsonOutputPath => "inventory.json";

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly ProcessingCadencePolicy _cadencePolicy;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;

    public InventoryOrchestrator(
        ILogger<InventoryOrchestrator> logger,
        ICheckpointingServiceFactory checkpointingFactory,
        IPlatformMetrics? metrics = null,
        ProcessingCadencePolicy? cadencePolicy = null)
    {
        _logger = logger;
        _checkpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        _metrics = metrics;
        _cadencePolicy = cadencePolicy ?? new ProcessingCadencePolicy();
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
        var checkpointing = _checkpointingFactory.Create(state);
        var cursorIdentity = StateCursorIdentity.Build("inventory", moduleName);

        var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(context.SourceEndpoint?.ResolvedUrl ?? "unknown");
        var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? "unknown";
        var csvOutputPath = OrgCsvOutputPathFor(orgSlug);
        var jsonOutputPath = OrgJsonOutputPathFor(orgSlug);
        var aggregateJsonPath = JsonOutputPath;

        _logger.LogInformation("Inventory orchestrator starting for job {JobId}, module {Module}.", job.JobId, moduleName);

        // Read checkpoint — presence means a previous run was interrupted.
        var lastCompleted = (await checkpointing.ReadCursorAsync(cursorIdentity, ct).ConfigureAwait(false))?.LastProcessed;
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

            // Push snapshot and metrics immediately so late-joining clients (those that
            // call GET /jobs/{id}/bootstrap after Job.Ready) see the previously-completed
            // projects before the first new project completes.
            if (orgProjectData.Count > 0)
            {
                var catchupSnapshots = BuildScopedOrganisations(orgProjectData, context.SourceEndpoint?.ResolvedUrl);
                PushAggregateMetrics(metricsStore, orgProjectData, catchupSnapshots);
                PushSnapshot(snapshotStore, orgProjectData, catchupSnapshots);
            }
        }

        var checkpointInterval = TimeSpan.FromSeconds(checkpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;
        var lastCompletedProjectKey = lastCompleted;
        string? lastCompletedProjectUrl = context.SourceEndpoint?.ResolvedUrl;
        string? lastCompletedProjectName = context.Project;

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
            lastCompletedProjectKey = projectKey;
            lastCompletedProjectUrl = evt.Url;
            lastCompletedProjectName = evt.ProjectName;

            var snapshots = BuildScopedOrganisations(orgProjectData, context.SourceEndpoint?.ResolvedUrl);
            PushAggregateMetrics(metricsStore, orgProjectData, snapshots);
            PushSnapshot(snapshotStore, orgProjectData, snapshots);

            // Checkpoint cursor at configured interval for resume support.
            if (_cadencePolicy.ShouldPersist(
                DateTimeOffset.UtcNow,
                new DateTimeOffset(lastCheckpoint, TimeSpan.Zero),
                processedSincePersist: 1,
                minimumBatchSize: 1,
                maxInterval: checkpointInterval))
            {
                await checkpointing.WriteCursorAsync(cursorIdentity, new CursorEntry
                {
                    LastProcessed = evt.ProjectName,
                    Stage = CursorStage.Completed,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct).ConfigureAwait(false);
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

        if (!string.IsNullOrWhiteSpace(lastCompletedProjectKey) &&
            !string.IsNullOrWhiteSpace(lastCompletedProjectUrl) &&
            !string.IsNullOrWhiteSpace(lastCompletedProjectName))
        {
            await checkpointing.WriteCursorAsync(cursorIdentity, new CursorEntry
            {
                LastProcessed = lastCompletedProjectName!,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }

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
        _ = state;
        _ = moduleName;

        var existingCsv = await store.ReadAsync(CsvOutputPath, ct).ConfigureAwait(false);
        if (existingCsv is null)
            return null;

        var completedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        return completedKeys.Count > 0 ? completedKeys : null;
    }

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
            catch (JsonException)
            {
                // Ignore malformed aggregate JSON and rebuild it from current data.
            }
        }

        foreach (var kvp in currentOrgData)
        {
            mergedData[kvp.Key] = kvp.Value.Select(p =>
                (p.Project, p.WorkItems, p.Revisions, p.Repos, p.IsComplete, p.Error)).ToList();
        }

        var organisations = mergedData.Select(kvp =>
        {
            var projects = kvp.Value.Select(p => new ProjectInventory
            {
                Name = p.Item1,
                WorkItems = p.Item2,
                Revisions = p.Item3,
                Repos = p.Item4,
                IsComplete = p.Item5,
                Error = p.Item6
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
        await store.WriteAsync(aggregatePath, json, ct).ConfigureAwait(false);
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
                                WorkItemsTotal = p.WorkItems,
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

