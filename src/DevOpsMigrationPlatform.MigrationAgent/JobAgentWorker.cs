using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
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
               moduleScopeFactory, httpClientFactory, logger, endpointConverter, organisationConverter)
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
        switch (job.Kind)
        {
            case JobKind.Export:
            case JobKind.Import:
            case JobKind.Migrate:
            case JobKind.Prepare:
                await OnMigrationJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            case JobKind.Inventory:
            case JobKind.Dependencies:
                await OnDiscoveryJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            default:
                _logger.LogError(
                    "Unknown job kind {JobKind} for lease — failing job {JobId}.",
                    job.Kind, job.JobId);
                await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
                break;
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
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        // Write config payload from the Job into the package before any config reads.
        await WriteConfigPayloadAsync(job, artefactStore, ct).ConfigureAwait(false);

        // Signal to live clients that the agent has the job and is starting up.
        ProgressSink.Emit(new ProgressEvent
        {
            Module = "Job",
            Stage = "Job.Received",
            Message = $"Job {job.JobId} acquired. Loading configuration.",
            Timestamp = DateTimeOffset.UtcNow
        });

        // Prepare mode writes a probe file to validate connectivity.
        if (job.Kind == JobKind.Prepare)
        {
            bool prepareFailed = false;
            try
            {
                _logger.LogInformation("Prepare mode — writing probe file for job {JobId}.", job.JobId);
                var probeContent = System.Text.Json.JsonSerializer.Serialize(new
                {
                    jobId = job.JobId,
                    timestamp = DateTimeOffset.UtcNow,
                    status = "ok"
                });
                await artefactStore.WriteAsync("prepare-probe.json", probeContent, ct).ConfigureAwait(false);

                ProgressSink.Emit(new ProgressEvent
                {
                    Module = "Prepare",
                    Stage = "Completed",
                    Message = "Probe file written successfully.",
                    Timestamp = DateTimeOffset.UtcNow
                });
                _logger.LogInformation("Prepare probe file written successfully for job {JobId}.", job.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Prepare probe failed for job {JobId}.", job.JobId);
                prepareFailed = true;
            }

            foreach (var flushable in _flushables)
                await flushable.FlushAsync().ConfigureAwait(false);

            await SignalTerminalAsync(controlPlane, leaseId, prepareFailed ? "fail" : "complete", ct).ConfigureAwait(false);
            return;
        }

        // Load migration-config.json from the package so modules can read Source/Target/Policies.
        IConfiguration packageConfig;
        try
        {
            packageConfig = await PackageConfigStore.ReadAsync(artefactStore, ct).ConfigureAwait(false);
        }
        catch (PackageConfigNotFoundException ex)
        {
            _logger.LogError(ex,
                "Config file not found in {PackageUri}. Re-submit the job via CLI.",
                job.Package.PackageUri);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            ActiveJobConfig.Clear();
            return;
        }

        ActiveJobConfig.PackageConfig = packageConfig;

        // Build the execution plan and push it to the Control Plane so clients can
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
                var checkpointer = CheckpointingFactory.Create(stateStore);
                var phaseTracker = PhaseTrackingFactory.Create(stateStore);
                
                _logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors and plan file.", job.JobId);
                foreach (var module in MigrationModules)
                {
                    await checkpointer.DeleteCursorAsync(module.Name, ct).ConfigureAwait(false);
                    _logger.LogDebug("Deleted cursor for module {Module}.", module.Name);
                }
                await phaseTracker.DeletePhaseRecordAsync(ct).ConfigureAwait(false);

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
            ActiveJobConfig.Clear();
            return;
        }

        // Create a per-job DI scope AFTER PackageConfig is set. Singleton tools (e.g.
        // IFieldTransformTool) are resolved fresh within this scope so their IOptions<T>.Value
        // is read from migration-config.json, not from the empty appsettings.json at host startup.
        using var jobScope = _moduleScopeFactory.CreateScope();
        var jobModules = jobScope.ServiceProvider.GetServices<IModule>().ToList();

        try
        {
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

            var isBoth = job.Kind == JobKind.Migrate;
            var phaseRecord = isBoth
                ? await phaseTracker.ReadPhaseRecordAsync(ct).ConfigureAwait(false)
                : new JobPhaseRecord();

            var runExport = job.Kind == JobKind.Export || (isBoth && !phaseRecord.ExportCompleted);
            var runImport = job.Kind == JobKind.Import || (isBoth && !phaseRecord.ImportCompleted);

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
        } // end outer try
        finally
        {
            ActiveJobConfig.Clear();
        }
    }

    // ── Discovery execution ───────────────────────────────────────────────────

    private async Task OnDiscoveryJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        // Write config payload from the Job into the package before any config reads.
        await WriteConfigPayloadAsync(job, artefactStore, ct).ConfigureAwait(false);

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

        // Read migration-config.json from the package and extract DiscoveryOptions.
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

        // Route to the right modules by job kind.
        // JobKind.Inventory maps to "InventoryDiscovery" (multi-org standalone module).
        // Other kinds use name matching (e.g. "Dependencies" → DependencyDiscoveryModule).
        var targetModuleName = job.Kind switch
        {
            JobKind.Inventory => "InventoryDiscovery",
            _ => job.Kind.ToString()
        };
        var modulesToRun = MigrationModules
            .Where(m => m.Name.Equals(targetModuleName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // When running dependency analysis, prepend InventoryDiscovery if inventory.json is missing.
        if (job.Kind == JobKind.Dependencies)
        {
            var inventoryExists = await artefactStore.ExistsAsync("inventory.json", ct).ConfigureAwait(false);
            if (!inventoryExists)
            {
                _logger.LogInformation(
                    "No inventory.json found for dependency job {JobId} — prepending InventoryDiscovery module.", job.JobId);
                var inventoryModules = MigrationModules
                    .Where(m => m.Name.Equals("InventoryDiscovery", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                modulesToRun = inventoryModules.Concat(modulesToRun).ToList();
            }
        }

        if (modulesToRun.Count == 0)
        {
            _logger.LogError(
                "No module found for kind {JobKind} — failing job {JobId}.",
                job.Kind, job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        var context = new ExportContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = ProgressSink,
            MetricsStore = _metricsStore,
            SnapshotStore = _snapshotStore,
            Organisations = organisations
        };

        bool failed = false;
        foreach (var module in modulesToRun)
        {
            try
            {
                _logger.LogInformation(
                    "Running discovery module {Module} for job {JobId}.", module.Name, job.JobId);
                await module.ExportAsync(context, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Discovery module {Module} completed for job {JobId}.", module.Name, job.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Discovery module {Module} failed for job {JobId}.", module.Name, job.JobId);
                failed = true;
                break;
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
        public DiscoveryOptions? MigrationPlatform { get; set; }
    }
}
