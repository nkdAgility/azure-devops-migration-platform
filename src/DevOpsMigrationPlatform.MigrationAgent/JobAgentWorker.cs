using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Unified background worker that polls <c>GET /agents/lease</c> for any pending <see cref="Job"/>,
/// then dispatches to the appropriate execution path based on the concrete job type:
/// <list type="bullet">
///   <item><see cref="MigrationJob"/> — runs <see cref="IModule"/> pipeline (export/import).</item>
///   <item><see cref="DiscoveryJob"/> — runs <see cref="IDiscoveryModule"/> pipeline (inventory/dependencies).</item>
/// </list>
/// Replaces the previous separate <c>MigrationAgentWorker</c> and <c>DiscoveryAgentWorker</c>,
/// eliminating the dual-queue / dual-lease-endpoint duplication and fixing a latent
/// concurrency bug where both workers shared <see cref="ActiveLeaseState"/> and
/// <see cref="ActivePackageState"/> singletons.
/// </summary>
public sealed class JobAgentWorker : BackgroundService
{
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly IEnumerable<IModule> _migrationModules;
    private readonly IEnumerable<IDiscoveryModule> _discoveryModules;
    private readonly IPackageStoreFactory _packageStoreFactory;
    private readonly IProgressSink _progressSink;
    private readonly ActiveLeaseState _leaseState;
    private readonly ActivePackageState _packageState;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IPhaseTrackingServiceFactory _phaseTrackingFactory;
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
        ILogger<JobAgentWorker> logger,
        PolymorphicEndpointOptionsConverter? endpointConverter = null)
    {
        _migrationModules = migrationModules;
        _discoveryModules = discoveryModules;
        _packageStoreFactory = packageStoreFactory;
        _progressSink = progressSink;
        _leaseState = leaseState;
        _packageState = packageState;
        _httpClientFactory = httpClientFactory;
        _checkpointingFactory = checkpointingFactory;
        _phaseTrackingFactory = phaseTrackingFactory;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        if (endpointConverter is not null)
            _jsonOptions.Converters.Add(endpointConverter);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job Agent Worker started — polling for jobs.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndExecuteAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in agent poll loop; retrying in 10 s.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Job Agent Worker stopping.");
    }

    private async Task PollAndExecuteAsync(CancellationToken ct)
    {
        using var controlPlane = _httpClientFactory.CreateClient("ControlPlane");

        using var leaseResponse = await controlPlane
            .GetAsync("/agents/lease", ct)
            .ConfigureAwait(false);

        if (leaseResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            return;
        }

        leaseResponse.EnsureSuccessStatusCode();

        var lease = await leaseResponse.Content
            .ReadFromJsonAsync<AgentLeaseResponse>(_jsonOptions, ct)
            .ConfigureAwait(false);

        if (lease is null) return;

        _logger.LogInformation(
            "Acquired lease {LeaseId} for job {JobId} ({JobType})",
            lease.LeaseId, lease.Job.JobId, lease.Job.GetType().Name);

        _leaseState.CurrentLeaseId = lease.LeaseId;
        _packageState.CurrentJobId = lease.Job.JobId;

        switch (lease.Job)
        {
            case MigrationJob migrationJob:
                await ExecuteMigrationAsync(migrationJob, controlPlane, lease.LeaseId, ct)
                    .ConfigureAwait(false);
                break;

            case DiscoveryJob discoveryJob:
                await ExecuteDiscoveryAsync(discoveryJob, controlPlane, lease.LeaseId, ct)
                    .ConfigureAwait(false);
                break;

            default:
                _logger.LogError(
                    "Unknown job type {JobType} for lease {LeaseId} — failing job.",
                    lease.Job.GetType().Name, lease.LeaseId);
                await SignalTerminalAsync(controlPlane, lease.LeaseId, "fail", ct).ConfigureAwait(false);
                break;
        }

        _leaseState.CurrentLeaseId = null;
        _packageState.Clear();
    }

    // ── Migration execution ───────────────────────────────────────────────────

    private async Task ExecuteMigrationAsync(
        MigrationJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = _packageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        _packageState.CurrentStore = artefactStore;

        var checkpointer = _checkpointingFactory.Create(stateStore);
        var phaseTracker = _phaseTrackingFactory.Create(stateStore);

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in _migrationModules)
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

                _progressSink.Emit(new ProgressEvent
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

            var prepareTerminal = prepareFailed ? "fail" : "complete";
            await SignalTerminalAsync(controlPlane, leaseId, prepareTerminal, ct).ConfigureAwait(false);
            return;
        }

        var exportContext = new ExportContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = _progressSink
        };
        var importContext = new ImportContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = _progressSink
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
                foreach (var module in _migrationModules)
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
                foreach (var module in _migrationModules)
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

    private async Task ExecuteDiscoveryAsync(
        DiscoveryJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = _packageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        _packageState.CurrentStore = artefactStore;

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation(
                "ForceFresh requested for discovery job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in _discoveryModules)
            {
                var cursorPath = $"Checkpoints/{module.Name}.cursor.json";
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
            ProgressSink = _progressSink
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

    // ── Shared ────────────────────────────────────────────────────────────────

    private async Task SignalTerminalAsync(
        HttpClient controlPlane, string leaseId, string terminal, CancellationToken ct)
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await controlPlane
                    .PostAsync($"/agents/lease/{leaseId}/{terminal}", content: null, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Terminal signal attempt {Attempt}/{Max} failed for lease {LeaseId}; retrying in {Delay} s.",
                    attempt, maxAttempts, leaseId, delay.TotalSeconds);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
        }

        _logger.LogError(
            "Failed to signal terminal state for lease {LeaseId} after {Max} attempts.",
            leaseId, maxAttempts);
    }

    private sealed record AgentLeaseResponse(string LeaseId, Job Job);
}
