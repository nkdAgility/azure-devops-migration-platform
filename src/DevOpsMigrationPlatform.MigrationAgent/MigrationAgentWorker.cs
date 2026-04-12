using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.JobEngine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Background worker that polls the control plane for available <see cref="MigrationJob"/>s,
/// acquires a lease, executes the Job Engine, and reports progress.
/// See docs/migration-agent.md for the full lease protocol.
/// </summary>
public sealed class MigrationAgentWorker : BackgroundService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IEnumerable<IModule> _modules;
    private readonly IPackageStoreFactory _packageStoreFactory;
    private readonly IProgressSink _progressSink;
    private readonly ActiveLeaseState _leaseState;
    private readonly ActivePackageState _packageState;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MigrationAgentWorker> _logger;

    public MigrationAgentWorker(
        IEnumerable<IModule> modules,
        IPackageStoreFactory packageStoreFactory,
        IProgressSink progressSink,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IHttpClientFactory httpClientFactory,
        ILogger<MigrationAgentWorker> logger)
    {
        _modules = modules;
        _packageStoreFactory = packageStoreFactory;
        _progressSink = progressSink;
        _leaseState = leaseState;
        _packageState = packageState;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration Agent started — polling for jobs.");

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

        _logger.LogInformation("Migration Agent stopping.");
    }

    private async Task PollAndExecuteAsync(CancellationToken ct)
    {
        using var controlPlane = _httpClientFactory.CreateClient("ControlPlane");

        // 1. Poll for a leased job (long-poll, 30 s server-side timeout).
        using var leaseResponse = await controlPlane
            .GetAsync("/agents/lease", ct)
            .ConfigureAwait(false);

        // 204 = no pending job; back off briefly and try again.
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
            "Acquired lease {LeaseId} for job {JobId} (mode={Mode})",
            lease.LeaseId, lease.Job.JobId, lease.Job.Mode);

        _leaseState.CurrentLeaseId = lease.LeaseId;

        // 2. Build stores from job artefacts URI via IPackageStoreFactory.
        var (artefactStore, stateStore) = _packageStoreFactory.Create(
            lease.Job.Artefacts.PackageUri ?? ".");

        // Publish the store so package-writing sinks (loggers, progress) can access it.
        _packageState.CurrentStore = artefactStore;

        var checkpointer = new CheckpointingService(stateStore);
        var phaseTracker = new PhaseTrackingService(stateStore);

        // 3. If ForceFresh, delete all module cursors and phase record before running (idmap preserved).
        if (lease.Job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors.", lease.Job.JobId);
            foreach (var module in _modules)
            {
                await checkpointer.DeleteCursorAsync(module.Name, ct).ConfigureAwait(false);
                _logger.LogDebug("Deleted cursor for module {Module}.", module.Name);
            }
            await phaseTracker.DeletePhaseRecordAsync(ct).ConfigureAwait(false);
        }

        // 4. Build contexts.
        var exportContext = new ExportContext
        {
            Job = lease.Job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = _progressSink
        };
        var importContext = new ImportContext
        {
            Job = lease.Job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = _progressSink
        };

        // 5. Run phases according to mode, respecting Both-mode phase tracking.
        var isBoth = string.Equals(lease.Job.Mode, "Both", StringComparison.OrdinalIgnoreCase);
        var phaseRecord = isBoth
            ? await phaseTracker.ReadPhaseRecordAsync(ct).ConfigureAwait(false)
            : new JobPhaseRecord();

        var runExport = string.Equals(lease.Job.Mode, "Export", StringComparison.OrdinalIgnoreCase)
            || (isBoth && !phaseRecord.ExportCompleted);
        var runImport = string.Equals(lease.Job.Mode, "Import", StringComparison.OrdinalIgnoreCase)
            || (isBoth && !phaseRecord.ImportCompleted);

        if (isBoth && !runExport)
            _logger.LogInformation("Export phase already completed for job {JobId} — skipping.", lease.Job.JobId);
        if (isBoth && !runImport)
            _logger.LogInformation("Import phase already completed for job {JobId} — skipping.", lease.Job.JobId);

        bool failed = false;
        try
        {
            if (runExport)
            {
                foreach (var module in _modules)
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
                foreach (var module in _modules)
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
            _logger.LogError(ex, "Job {JobId} failed during module execution.", lease.Job.JobId);
            failed = true;
        }

        // 5. Signal terminal state — retry with back-off so the lease is not orphaned.
        var terminal = failed ? "fail" : "complete";
        await SignalTerminalAsync(controlPlane, lease.LeaseId, terminal, ct).ConfigureAwait(false);
        _packageState.CurrentStore = null;
        _leaseState.CurrentLeaseId = null;
    }

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
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2); // exponential back-off
            }
        }

        _logger.LogError(
            "Failed to signal terminal state for lease {LeaseId} after {Max} attempts.",
            leaseId, maxAttempts);
    }

    private sealed record AgentLeaseResponse(string LeaseId, MigrationJob Job);
}

