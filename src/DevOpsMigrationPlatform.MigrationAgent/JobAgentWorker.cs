using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// MigrationAgent-specific worker that handles ADO and Simulated source jobs.
/// Inherits the module-pipeline infrastructure (stores, checkpointing, ForceFresh,
/// module loop) from <see cref="ModulePipelineWorkerBase"/>, and overrides
/// <see cref="AgentWorkerBase.OnMigrationJobAsync"/> to add Both, Import, and Prepare modes.
/// </summary>
public sealed class JobAgentWorker : ModulePipelineWorkerBase
{
    private readonly IEnumerable<IDiscoveryModule> _discoveryModules;
    private readonly IJobMetricsStore _metricsStore;
    private readonly IJobSnapshotStore _snapshotStore;
    private readonly PackageProgressSink _packageProgressSink;
    private readonly PackageLoggerProvider _packageLoggerProvider;
    private readonly ILogger<JobAgentWorker> _logger;

    public JobAgentWorker(
        IEnumerable<IModule> migrationModules,
        IEnumerable<IDiscoveryModule> discoveryModules,
        IPackageStoreFactory packageStoreFactory,
        IProgressSink progressSink,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IHttpClientFactory httpClientFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        IJobMetricsStore metricsStore,
        IJobSnapshotStore snapshotStore,
        PackageProgressSink packageProgressSink,
        PackageLoggerProvider packageLoggerProvider,
        ILogger<JobAgentWorker> logger,
        PolymorphicEndpointOptionsConverter? endpointConverter = null)
        : base(migrationModules, packageStoreFactory, progressSink, checkpointingFactory,
               phaseTrackingFactory, leaseState, packageState, httpClientFactory, logger,
               endpointConverter)
    {
        _discoveryModules = discoveryModules;
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _packageProgressSink = packageProgressSink;
        _packageLoggerProvider = packageLoggerProvider;
        _logger = logger;
    }

    protected override string[] Capabilities => new[] { "ado", "simulated" };

    protected override async Task OnPostJobFlushAsync()
    {
        await _packageProgressSink.FlushAsync().ConfigureAwait(false);
        await _packageLoggerProvider.FlushAsync().ConfigureAwait(false);
    }

    // ── Migration execution ───────────────────────────────────────────────────

    protected override async Task OnMigrationJobAsync(
        MigrationJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        var checkpointer = CheckpointingFactory.Create(stateStore);
        var phaseTracker = PhaseTrackingFactory.Create(stateStore);

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in MigrationModules)
            {
                await checkpointer.DeleteCursorAsync(module.Name, ct).ConfigureAwait(false);
                _logger.LogDebug("Deleted cursor for module {Module}.", module.Name);
            }
            await phaseTracker.DeletePhaseRecordAsync(ct).ConfigureAwait(false);
        }

        if (string.Equals(job.Mode, "Prepare", StringComparison.OrdinalIgnoreCase))
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

            // Flush before signalling terminal so that progress/log records written during
            // Prepare are persisted to the package before the control plane notifies the CLI
            // (which may kill the agent process immediately after seeing completion).
            await _packageProgressSink.FlushAsync().ConfigureAwait(false);
            await _packageLoggerProvider.FlushAsync().ConfigureAwait(false);

            var prepareTerminal = prepareFailed ? "fail" : "complete";
            await SignalTerminalAsync(controlPlane, leaseId, prepareTerminal, ct).ConfigureAwait(false);
            return;
        }

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

        var isBoth = string.Equals(job.Mode, "Both", StringComparison.OrdinalIgnoreCase);
        var phaseRecord = isBoth
            ? await phaseTracker.ReadPhaseRecordAsync(ct).ConfigureAwait(false)
            : new JobPhaseRecord();

        var runExport = string.Equals(job.Mode, "Export", StringComparison.OrdinalIgnoreCase)
            || (isBoth && !phaseRecord.ExportCompleted);
        var runImport = string.Equals(job.Mode, "Import", StringComparison.OrdinalIgnoreCase)
            || (isBoth && !phaseRecord.ImportCompleted);

        if (isBoth && !runExport)
            _logger.LogInformation("Export phase already completed for job {JobId} — skipping.", job.JobId);
        if (isBoth && !runImport)
            _logger.LogInformation("Import phase already completed for job {JobId} — skipping.", job.JobId);

        bool failed = false;
        try
        {
            if (runExport)
            {
                foreach (var module in MigrationModules)
                {
                    _logger.LogInformation("Running module {Module}.ExportAsync", module.Name);
                    await module.ExportAsync(exportContext, ct).ConfigureAwait(false);
                }
                if (isBoth)
                {
                    await phaseTracker.WritePhaseRecordAsync(
                        new JobPhaseRecord { ExportCompleted = true, ImportCompleted = phaseRecord.ImportCompleted, UpdatedAt = DateTimeOffset.UtcNow },
                        ct).ConfigureAwait(false);
                }
            }

            if (runImport)
            {
                foreach (var module in MigrationModules)
                {
                    _logger.LogInformation("Running module {Module}.ImportAsync", module.Name);
                    await module.ImportAsync(importContext, ct).ConfigureAwait(false);
                }
                if (isBoth)
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
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Discovery execution ───────────────────────────────────────────────────

    protected override async Task OnDiscoveryJobAsync(
        DiscoveryJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation(
                "ForceFresh requested for discovery job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in _discoveryModules)
            {
                var cursorPath = PackagePaths.CursorFile(module.Name);
                try { await stateStore.DeleteAsync(cursorPath, ct).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete cursor for discovery module {Module}.", module.Name);
                }
            }
        }

        var modulesToRun = job.DiscoveryType == DiscoveryJobType.Both
            ? _discoveryModules.OrderBy(m => (int)m.DiscoveryType).ToList()
            : _discoveryModules.Where(m => m.DiscoveryType == job.DiscoveryType).ToList();

        // When running dependency analysis, auto-run inventory first if inventory.json
        // does not yet exist in the package. The dependency module reads inventory.json
        // for pre-counts; without it, the analysis has no baseline.
        if (job.DiscoveryType == DiscoveryJobType.Dependencies)
        {
            var inventoryExists = await artefactStore.ExistsAsync("inventory.json", ct).ConfigureAwait(false);
            if (!inventoryExists)
            {
                _logger.LogInformation(
                    "No inventory.json found for dependency job {JobId} — prepending inventory module.", job.JobId);
                var inventoryModules = _discoveryModules
                    .Where(m => m.DiscoveryType == DiscoveryJobType.Inventory)
                    .ToList();
                modulesToRun = inventoryModules.Concat(modulesToRun).ToList();
            }
        }

        if (modulesToRun.Count == 0)
        {
            _logger.LogError(
                "No discovery module found for type {DiscoveryType} — failing job {JobId}.",
                job.DiscoveryType, job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        var context = new DiscoveryContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = ProgressSink,
            MetricsStore = _metricsStore,
            SnapshotStore = _snapshotStore
        };

        bool failed = false;
        foreach (var module in modulesToRun)
        {
            try
            {
                _logger.LogInformation(
                    "Running discovery module {Module} for job {JobId}.", module.Name, job.JobId);
                await module.RunAsync(context, ct).ConfigureAwait(false);
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
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }
}
