// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// MigrationAgent-specific worker that handles ADO and Simulated source jobs.
/// Inherits the module-pipeline infrastructure (stores, checkpointing, ForceFresh,
/// module loop) from <see cref="ModulePipelineWorkerBase"/>, and overrides
/// <see cref="AgentWorkerBase.OnJobAsync"/> to dispatch on <see cref="Job.Kind"/>.
/// </summary>
public sealed class JobAgentWorker : ModulePipelineWorkerBase
{
    private static readonly ActivitySource s_discoveryActivity = new(WellKnownActivitySourceNames.Discovery);
    private readonly IJobMetricsStore _metricsStore;
    private readonly IJobSnapshotStore _snapshotStore;
    private readonly IEnumerable<IFlushable> _flushables;
    private readonly IServiceScopeFactory _moduleScopeFactory;
    private readonly IPackagePreparer _packagePreparer;
    private readonly IJobExecutionPlanBuilder _planBuilder;
    private readonly IJobPlanExecutor _planExecutor;
    private readonly IControlPlaneTelemetryClient _telemetryClient;
    private readonly ILogger<JobAgentWorker> _logger;

    public JobAgentWorker(
        IEnumerable<IModule> migrationModules,
        IPackageStoreFactory packageStoreFactory,
        IPackagePreparer packagePreparer,
        IProgressSink progressSink,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IJobConfiguration activeJobConfig,
        IActiveJobState activeJobState,
        IPackageConfigStore packageConfigStore,
        IServiceScopeFactory moduleScopeFactory,
        IHttpClientFactory httpClientFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        IJobMetricsStore metricsStore,
        IJobSnapshotStore snapshotStore,
        IEnumerable<IFlushable> flushables,
        IJobExecutionPlanBuilder planBuilder,
        IJobPlanExecutor planExecutor,
        IControlPlaneTelemetryClient telemetryClient,
        ILogger<JobAgentWorker> logger,
        PolymorphicEndpointOptionsConverter? endpointConverter = null,
        PolymorphicOrganisationEntryConverter? organisationConverter = null)
        : base(migrationModules, packageStoreFactory, progressSink, checkpointingFactory,
               phaseTrackingFactory, leaseState, packageState, activeJobConfig, packageConfigStore,
               moduleScopeFactory, httpClientFactory, logger, activeJobState, endpointConverter, organisationConverter)
    {
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _flushables = flushables;
        _moduleScopeFactory = moduleScopeFactory;
        _packagePreparer = packagePreparer;
        _planBuilder = planBuilder;
        _planExecutor = planExecutor;
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    protected override ConnectorType[] Capabilities => new[] { ConnectorType.AzureDevOps, ConnectorType.Simulated };

    protected override async Task OnPostJobFlushAsync()
    {
        foreach (var flushable in _flushables)
            await flushable.FlushAsync().ConfigureAwait(false);
    }

    // ── Job dispatch ─────────────────────────────────────────────────────────

    protected override async Task OnJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        // ── Shared preamble: package stores, config write, config load ────────
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        // Write config payload from the Job into the package before any config reads.
        await WriteConfigPayloadAsync(job, artefactStore, ct).ConfigureAwait(false);

        // Load migration-config.json so singleton services (ActiveJobSourceEndpointInfo,
        // IOptions<T> bound from PackageConfig, etc.) resolve per-job values.
        IConfiguration? packageConfig = null;
        try
        {
            packageConfig = await PackageConfigStore.ReadAsync(artefactStore, ct).ConfigureAwait(false);
            ActiveJobConfig.PackageConfig = packageConfig;
        }
        catch (PackageConfigNotFoundException ex)
        {
            if (job.Kind == JobKind.Prepare)
            {
                // Prepare jobs only write a probe file — config is not required.
                _logger.LogDebug(ex, "Config not found for Prepare job {JobId} — proceeding without it.", job.JobId);
            }
            else
            {
                _logger.LogError(ex,
                    "Config file not found in {PackageUri} for job {JobId}. Re-submit the job via CLI.",
                    job.Package.PackageUri, job.JobId);
                await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
                ActiveJobConfig.Clear();
                return;
            }
        }

        // Signal to live clients that the agent has the job and is starting up.
        ProgressSink.Emit(new ProgressEvent
        {
            Module = "Job",
            Stage = "Job.Received",
            Message = $"Job {job.JobId} acquired. Loading configuration.",
            Timestamp = DateTimeOffset.UtcNow
        });

        // ── Dispatch to kind-specific handler ─────────────────────────────────
        try
        {
            switch (job.Kind)
            {
                case JobKind.Export:
                case JobKind.Import:
                case JobKind.Migrate:
                case JobKind.Prepare:
                    await OnMigrationJobAsync(job, controlPlane, leaseId, artefactStore, stateStore, ct).ConfigureAwait(false);
                    break;

                case JobKind.Inventory:
                case JobKind.Dependencies:
                    await OnDiscoveryJobAsync(job, controlPlane, leaseId, artefactStore, stateStore, ct).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogError(
                        "Unknown job kind {JobKind} for lease — failing job {JobId}.",
                        job.Kind, job.JobId);
                    await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            ActiveJobConfig.Clear();
        }
    }

    // ── Migration execution ───────────────────────────────────────────────────

    /// <summary>
    /// If <see cref="Job.ConfigPayload"/> is set, writes it to <c>migration-config.json</c>
    /// in the package before any module reads the config.
    /// Resume behaviour: if the file already exists and ForceFresh is not set, verifies that
    /// the Source and Target identity fields are unchanged before overwriting. An incompatible
    /// config (different source URL/project or target URL/project) is rejected with a clear error.
    /// Use <see cref="ResumeMode.ForceFresh"/> to restart with a completely new configuration.
    /// </summary>
    private static async Task WriteConfigPayloadAsync(
        Job job, IArtefactStore artefactStore, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.ConfigPayload))
            return;

        var forceFresh = job.Resume?.Mode == DevOpsMigrationPlatform.Abstractions.Jobs.ResumeMode.ForceFresh;
        var exists = await artefactStore.ExistsAsync(PackagePaths.MigrationConfigFileName, ct).ConfigureAwait(false);

        if (exists && !forceFresh)
        {
            // Resume mode: verify the Source and Target endpoints are unchanged.
            // A compatible re-submission overwrites the config (picking up any non-identity
            // changes such as module settings) while preserving cursor state.
            var existingJson = await artefactStore.ReadAsync(PackagePaths.MigrationConfigFileName, ct).ConfigureAwait(false);
            var mismatch = GetSourceTargetMismatch(existingJson ?? string.Empty, job.ConfigPayload);
            if (mismatch != null)
                throw new InvalidOperationException(
                    $"Cannot resume migration: {mismatch}. " +
                    "Use --force-fresh to restart with the updated configuration.");
            // Compatible — fall through and overwrite (cursor state is preserved separately).
        }

        await artefactStore.WriteAsync(
            PackagePaths.MigrationConfigFileName, job.ConfigPayload, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compares the Source and Target identity fields (Type, Url/Collection, Project) between
    /// the existing <c>migration-config.json</c> and the incoming config payload.
    /// Returns <c>null</c> when the configs are compatible; otherwise returns a human-readable
    /// description of the first mismatch found.
    /// If either JSON cannot be parsed, returns <c>null</c> (allow the write).
    /// </summary>
    private static string? GetSourceTargetMismatch(string existingJson, string newJson)
    {
        static (string? type, string? url, string? project) ExtractEndpoint(JsonElement root, string role)
        {
            if (!root.TryGetProperty("MigrationPlatform", out var platform))
                return (null, null, null);
            if (!platform.TryGetProperty(role, out var endpoint))
                return (null, null, null);
            var type = endpoint.TryGetProperty("Type", out var t) ? t.GetString() : null;
            var url = endpoint.TryGetProperty("Url", out var u) ? u.GetString()
                        : endpoint.TryGetProperty("Collection", out var c) ? c.GetString()
                        : null;
            var project = endpoint.TryGetProperty("Project", out var p) ? p.GetString() : null;
            return (type, url, project);
        }

        try
        {
            using var existingDoc = JsonDocument.Parse(existingJson);
            using var newDoc = JsonDocument.Parse(newJson);

            var (eSrcType, eSrcUrl, eSrcProject) = ExtractEndpoint(existingDoc.RootElement, "Source");
            var (nSrcType, nSrcUrl, nSrcProject) = ExtractEndpoint(newDoc.RootElement, "Source");

            if (!StringComparer.OrdinalIgnoreCase.Equals(eSrcType, nSrcType) ||
                !StringComparer.OrdinalIgnoreCase.Equals(eSrcUrl, nSrcUrl) ||
                !StringComparer.OrdinalIgnoreCase.Equals(eSrcProject, nSrcProject))
                return $"Source changed from '{eSrcType}:{eSrcUrl}/{eSrcProject}' to '{nSrcType}:{nSrcUrl}/{nSrcProject}'";

            var (eTgtType, eTgtUrl, eTgtProject) = ExtractEndpoint(existingDoc.RootElement, "Target");
            var (nTgtType, nTgtUrl, nTgtProject) = ExtractEndpoint(newDoc.RootElement, "Target");

            if (!StringComparer.OrdinalIgnoreCase.Equals(eTgtType, nTgtType) ||
                !StringComparer.OrdinalIgnoreCase.Equals(eTgtUrl, nTgtUrl) ||
                !StringComparer.OrdinalIgnoreCase.Equals(eTgtProject, nTgtProject))
                return $"Target changed from '{eTgtType}:{eTgtUrl}/{eTgtProject}' to '{nTgtType}:{nTgtUrl}/{nTgtProject}'";

            return null; // compatible
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // unparseable JSON — allow write (agent will catch the real error later)
        }
    }

    private async Task OnMigrationJobAsync(
        Job job, HttpClient controlPlane, string leaseId,
        IArtefactStore artefactStore, IStateStore stateStore, CancellationToken ct)
    {
        // PackageConfig is already loaded by OnJobAsync — use it directly.
        var packageConfig = ActiveJobConfig.PackageConfig!;

        // Build the execution planand push it to the Control Plane so clients can
        // see the ordered task list immediately via GET /jobs/{id}/bootstrap.
        JobTaskList executionPlan;
        try
        {
            ProgressSink.Emit(new ProgressEvent
            {
                Module = "Job",
                Stage = "Job.Planning",
                Message = "Building execution plan from package configuration.",
                Timestamp = DateTimeOffset.UtcNow
            });

            // Handle ForceFresh BEFORE loading the plan — delete cursors, phase record, and plan file.
            if (job.Resume?.Mode == ResumeMode.ForceFresh)
            {
                var freshCheckpointer = CheckpointingFactory.Create(stateStore);
                var freshPhaseTracker = PhaseTrackingFactory.Create(stateStore);

                _logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors and plan file.", job.JobId);
                foreach (var module in MigrationModules)
                {
                    await freshCheckpointer.DeleteCursorAsync(module.Name, ct).ConfigureAwait(false);
                    _logger.LogDebug("Deleted cursor for module {Module}.", module.Name);
                }
                await freshPhaseTracker.DeletePhaseRecordAsync(ct).ConfigureAwait(false);

                // Delete the persisted plan file so a fresh plan is built.
                try
                {
                    await stateStore.DeleteAsync(PackagePaths.PlanFile, ct).ConfigureAwait(false);
                    _logger.LogDebug("Deleted plan file {Path}.", PackagePaths.PlanFile);
                }
                catch (System.IO.FileNotFoundException)
                {
                    // Plan file didn't exist — not an error.
                }
            }

            // Attempt to load persisted plan from package (resume scenario).
            var loadedPlan = await JobPlanExecutor.LoadOrResetAsync(stateStore, ct)
                .ConfigureAwait(false);

            if (loadedPlan is not null)
            {
                _logger.LogInformation(
                    "Loaded execution plan from package: {TaskCount} task(s), {PendingCount} pending, {CompletedCount} completed.",
                    loadedPlan.Tasks.Count,
                    loadedPlan.Tasks.Count(t => t.Status == JobTaskStatus.Pending),
                    loadedPlan.Tasks.Count(t => t.Status == JobTaskStatus.Completed));
                executionPlan = loadedPlan;
            }
            else
            {
                // No persisted plan — build fresh.
                var freshPlan = await _planBuilder
                    .BuildPlanAsync(packageConfig, job.Kind, artefactStore, stateStore, ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Built fresh execution plan: {TaskCount} task(s).",
                    freshPlan.Tasks.Count);

                // Persist the fresh plan immediately.
                var json = System.Text.Json.JsonSerializer.Serialize(freshPlan);
                await stateStore.WriteAsync(PackagePaths.PlanFile, json, ct).ConfigureAwait(false);

                executionPlan = freshPlan;
            }

            // Push plan to the control plane for display.
            await _telemetryClient.PushTaskListAsync(leaseId, executionPlan, ct).ConfigureAwait(false);

            ProgressSink.Emit(new ProgressEvent
            {
                Module = "Job",
                Stage = "Job.Ready",
                Message = $"Execution plan ready. {executionPlan.Tasks.Count} task(s) queued.",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            // Fatal — plan build failure means we can't proceed.
            _logger.LogError(ex, "Failed to build or load execution plan for job {JobId}.", job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        // Create a per-job DI scope AFTER PackageConfig is set. Singleton tools (e.g.
        // IFieldTransformTool) are resolved fresh within this scope so their IOptions<T>.Value
        // is read from migration-config.json, not from the empty appsettings.json at host startup.
        using var jobScope = _moduleScopeFactory.CreateScope();
        var jobModules = jobScope.ServiceProvider.GetServices<IModule>().ToList();

        var checkpointer = CheckpointingFactory.Create(stateStore);
        var phaseTracker = PhaseTrackingFactory.Create(stateStore);

        var exportContext = new ExportContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = ProgressSink
        };
        var importContext = new ImportContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = ProgressSink
        };
        var prepareContext = new PrepareContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = ProgressSink,
            TargetEndpoint = jobScope.ServiceProvider.GetRequiredService<ITargetEndpointInfo>()
        };

        var isBoth = job.Kind == JobKind.Migrate;
        var needsPhaseRecord = isBoth || job.Kind == JobKind.Import;
        var phaseRecord = needsPhaseRecord
            ? await phaseTracker.ReadPhaseRecordAsync(ct).ConfigureAwait(false)
            : new JobPhaseRecord();

        var runExport = job.Kind == JobKind.Export || (isBoth && !phaseRecord.ExportCompleted);
        var runImport = job.Kind == JobKind.Import || (isBoth && !phaseRecord.ImportCompleted);
        var runPrepare = job.Kind == JobKind.Prepare || (runImport && !phaseRecord.PrepareCompleted);

        if (isBoth && !runExport)
            _logger.LogInformation("Export phase already completed for job {JobId} — skipping.", job.JobId);
        if (isBoth && !runImport)
            _logger.LogInformation("Import phase already completed for job {JobId} — skipping.", job.JobId);

        // If PackagePath is set and we are about to import, extract the fixture into the
        // package store via IPackagePreparer. This is storage-backend agnostic — works for
        // FileSystem today and Azure Blob Storage in the future.
        if (runImport)
        {
            await _packagePreparer.PrepareForImportAsync(artefactStore, packageConfig, ct)
                .ConfigureAwait(false);
        }

        bool failed = false;
        try
        {
            if (runExport)
            {
                // Execute export phase using the plan executor (includes Inventory if needed).
                var moduleMap = jobModules.ToDictionary(m => m.Name, m => (IModule)m, StringComparer.OrdinalIgnoreCase);
                var exportOk = await _planExecutor.ExecuteExportPhaseAsync(
                    executionPlan, moduleMap, exportContext, stateStore, ct).ConfigureAwait(false);

                failed = !exportOk;

                if (isBoth && exportOk)
                {
                    await phaseTracker.WritePhaseRecordAsync(
                        new JobPhaseRecord { ExportCompleted = true, ImportCompleted = phaseRecord.ImportCompleted, UpdatedAt = DateTimeOffset.UtcNow },
                        ct).ConfigureAwait(false);
                }
            }

            if (runImport)
            {
                // Execute import phase using the plan executor.
                var moduleMap = jobModules.ToDictionary(m => m.Name, m => (IModule)m, StringComparer.OrdinalIgnoreCase);
                var importOk = await _planExecutor.ExecuteImportPhaseAsync(
                    executionPlan, moduleMap, importContext, stateStore, ct).ConfigureAwait(false);

                failed = !importOk;

                if (isBoth && importOk)
                {
                    await phaseTracker.WritePhaseRecordAsync(
                        new JobPhaseRecord { ExportCompleted = true, ImportCompleted = true, UpdatedAt = DateTimeOffset.UtcNow },
                        ct).ConfigureAwait(false);
                }
            }

            if (runPrepare)
            {
                var jobAnalysers = jobScope.ServiceProvider.GetServices<IAnalyser>().ToList();
                var probeContent = System.Text.Json.JsonSerializer.Serialize(new
                {
                    jobId = job.JobId,
                    timestamp = DateTimeOffset.UtcNow,
                    status = "ok"
                });
                await artefactStore.WriteAsync("prepare-probe.json", probeContent, ct).ConfigureAwait(false);

                var prepareModules = jobModules.Where(m => m.SupportsPrepare).ToList();
                var requiredAnalyserNames = prepareModules
                    .SelectMany(m => m.DependsOn.Where(d => d.AppliesToAnalyse).Select(d => d.ModuleName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var analyser in jobAnalysers.Where(a => requiredAnalyserNames.Contains(a.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    await analyser.AnalyseAsync(new AnalyseContext
                    {
                        Job = job,
                        ArtefactStore = artefactStore,
                        StateStore = stateStore,
                        ProgressSink = ProgressSink
                    }, ct).ConfigureAwait(false);
                }

                foreach (var module in prepareModules)
                {
                    await module.PrepareAsync(prepareContext, ct).ConfigureAwait(false);
                }

                if (needsPhaseRecord)
                {
                    await phaseTracker.WritePhaseRecordAsync(
                        new JobPhaseRecord
                        {
                            ExportCompleted = phaseRecord.ExportCompleted,
                            PrepareCompleted = true,
                            ImportCompleted = phaseRecord.ImportCompleted,
                            UpdatedAt = DateTimeOffset.UtcNow
                        },
                        ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed during module execution.", job.JobId);
            failed = true;
        }

        var terminal = failed ? "fail" : "complete";
        // Flush buffered sinks (progress.jsonl, agent.jsonl) BEFORE signalling completion.
        // The CLI kills this process as soon as it receives the terminal status from the
        // control plane. OnPostJobFlushAsync (base class) runs AFTER SignalTerminalAsync,
        // so without this pre-signal flush, async-batched sinks may never write their data.
        foreach (var flushable in _flushables)
            await flushable.FlushAsync().ConfigureAwait(false);
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Discovery execution ───────────────────────────────────────────────────

    private async Task OnDiscoveryJobAsync(
        Job job, HttpClient controlPlane, string leaseId,
        IArtefactStore artefactStore, IStateStore stateStore, CancellationToken ct)
    {
        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation(
                "ForceFresh requested for discovery job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in MigrationModules)
            {
                var cursorPath = PackagePaths.CursorFile(module.Name);
                try { await stateStore.DeleteAsync(cursorPath, ct).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete cursor for discovery module {Module}.", module.Name);
                }
            }
        }

        // Read migration-config.json from the package and extract discovery settings.
        var organisations = new List<ScopedOrganisationEndpoint>();
        var policies = new JobPolicies();
        try
        {
            var rawJson = await artefactStore.ReadAsync(PackagePaths.MigrationConfigFileName, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                var wrapper = JsonSerializer.Deserialize<DiscoveryConfigWrapper>(rawJson, AgentJsonOptions);
                if (wrapper?.MigrationPlatform?.Organisations is { Count: > 0 } orgs)
                {
                    organisations = orgs
                        .Where(o => o.Enabled)
                        .Select(o => new ScopedOrganisationEndpoint
                        {
                            Endpoint = o.ToEndpointOptions(),
                            Projects = new List<string>(o.Projects),
                            Scopes = o.Scopes.Select(s => new JobModuleScope
                            {
                                Type = s.Type,
                                Parameters = s.Parameters.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)
                            }).ToList()
                        })
                        .ToList();
                }
                if (wrapper?.MigrationPlatform?.Policies is { } p)
                    policies = new JobPolicies { MaxRetries = p.Retries.Max, MaxConcurrency = p.Throttle.MaxConcurrency, CheckpointIntervalSeconds = p.Checkpoints.Interval };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not read migration-config.json for discovery job {JobId}; organisations will be empty.",
                job.JobId);
        }

        if (organisations.Count == 0)
        {
            organisations.Add(new ScopedOrganisationEndpoint
            {
                Endpoint = new InlineMigrationEndpointOptions(new OrganisationEndpoint { Type = "Unknown", ResolvedUrl = string.Empty }),
                Projects = []
            });
        }

        using var jobScope = _moduleScopeFactory.CreateScope();
        var modulesToRun = jobScope.ServiceProvider.GetServices<IModule>().ToList();
        var analysersToRun = jobScope.ServiceProvider.GetServices<IAnalyser>().ToList();

        bool failed = false;
        if (job.Kind == JobKind.Inventory)
        {
            var inventoryModules = modulesToRun.Where(m => m.SupportsInventory).ToList();
            _logger.LogInformation("Starting multi-org inventory: {OrgCount} organisations", organisations.Count);
            long cumulativeInventoryOperations = 0;

            foreach (var (org, index) in organisations.Select((value, idx) => (value, idx + 1)))
            {
                var endpoint = org.Endpoint.ToOrganisationEndpoint();
                using var orgActivity = s_discoveryActivity.StartActivity("inventory.workitems");
                orgActivity?.SetTag("job.id", job.JobId);
                orgActivity?.SetTag("org.url", endpoint.ResolvedUrl);
                foreach (var module in inventoryModules)
                {
                    try
                    {
                        await module.InventoryAsync(new InventoryContext
                        {
                            Job = job,
                            ArtefactStore = artefactStore,
                            StateStore = stateStore,
                            ProgressSink = ProgressSink,
                            SourceEndpoint = endpoint,
                            Projects = org.Projects
                        }, ct).ConfigureAwait(false);
                        cumulativeInventoryOperations++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Organisation {OrgIndex}/{OrgCount} unreachable: {ErrorType}",
                            index,
                            organisations.Count,
                            ex.GetType().Name);
                        failed = true;
                    }
                }

                ProgressSink.Emit(new ProgressEvent
                {
                    Module = "Inventory",
                    Stage = "Inventory.OrgCompleted",
                    Message = $"Completed organisation {index}/{organisations.Count}",
                    Timestamp = DateTimeOffset.UtcNow,
                    Metrics = new JobMetrics
                    {
                        Migration = new MigrationCounters
                        {
                            Inventory = new ModulePhaseCounters
                            {
                                Completed = cumulativeInventoryOperations
                            }
                        }
                    }
                });
            }

            foreach (var analyser in analysersToRun.Where(a => a.Name.Equals("Inventory", StringComparison.OrdinalIgnoreCase)))
            {
                await analyser.AnalyseAsync(new AnalyseContext
                {
                    Job = job,
                    ArtefactStore = artefactStore,
                    StateStore = stateStore,
                    ProgressSink = ProgressSink
                }, ct).ConfigureAwait(false);
            }
        }
        else if (job.Kind == JobKind.Dependencies)
        {
            var dependencyAnalyser = analysersToRun.FirstOrDefault(a => a.Name.Equals("Dependencies", StringComparison.OrdinalIgnoreCase));
            if (dependencyAnalyser is null)
            {
                _logger.LogError("No dependency analyser registered for job {JobId}.", job.JobId);
                failed = true;
            }
            else
            {
                try
                {
                    await dependencyAnalyser.AnalyseAsync(new OrganisationsAnalyseContext
                    {
                        Job = job,
                        ArtefactStore = artefactStore,
                        StateStore = stateStore,
                        ProgressSink = ProgressSink,
                        Organisations = organisations
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Dependency analysis failed for job {JobId}.", job.JobId);
                    failed = true;
                }
            }
        }

        var terminal = failed ? "fail" : "complete";
        // Flush buffered sinks before signalling — the CLI kills this process on receipt.
        foreach (var flushable in _flushables)
            await flushable.FlushAsync().ConfigureAwait(false);
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    private sealed class DiscoveryConfigWrapper
    {
        public MigrationOptions? MigrationPlatform { get; set; }
    }

    private sealed class InlineMigrationEndpointOptions(OrganisationEndpoint endpoint) : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint() => endpoint;
        public override string GetResolvedUrl() => endpoint.ResolvedUrl;
    }
}
