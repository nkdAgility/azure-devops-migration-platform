using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.TfsMigrationAgent;

/// <summary>
/// TFS-specific agent worker. Inherits the polling loop and lease protocol from
/// <see cref="AgentWorkerBase"/>; implements TFS export via <see cref="TfsExportAgent"/>
/// and TFS discovery via the existing <see cref="IWorkItemDiscoveryService"/>.
/// Advertises <c>Capabilities = ["tfs"]</c> so only TFS jobs are acquired.
/// </summary>
public sealed class TfsJobAgentWorker : AgentWorkerBase
{
    private readonly IPackageStoreFactory _packageStoreFactory;
    private readonly IProgressSink _progressSink;
    private readonly ICheckpointingServiceFactory _checkpointingFactory;
    private readonly IPhaseTrackingServiceFactory _phaseTrackingFactory;
    private readonly IJobMetricsStore _metricsStore;
    private readonly IJobSnapshotStore _snapshotStore;
    private readonly PackageProgressSink _packageProgressSink;
    private readonly PackageLoggerProvider _packageLoggerProvider;
    private readonly ILogger<TfsJobAgentWorker> _logger;

    public TfsJobAgentWorker(
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
        ILogger<TfsJobAgentWorker> logger)
        : base(leaseState, packageState, httpClientFactory, logger)
    {
        _packageStoreFactory = packageStoreFactory;
        _progressSink = progressSink;
        _checkpointingFactory = checkpointingFactory;
        _phaseTrackingFactory = phaseTrackingFactory;
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _packageProgressSink = packageProgressSink;
        _packageLoggerProvider = packageLoggerProvider;
        _logger = logger;
    }

    protected override string[] Capabilities => new[] { "tfs" };

    protected override async Task OnPostJobFlushAsync()
    {
        await _packageProgressSink.FlushAsync().ConfigureAwait(false);
        await _packageLoggerProvider.FlushAsync().ConfigureAwait(false);
    }

    // ── Migration execution ───────────────────────────────────────────────────

    protected override async Task OnMigrationJobAsync(
        MigrationJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = _packageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        var checkpointer = _checkpointingFactory.Create(stateStore);

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation("ForceFresh requested for TFS job {JobId} — deleting cursors.", job.JobId);
            await checkpointer.DeleteCursorAsync("WorkItems", ct).ConfigureAwait(false);
        }

        // TFS agent currently supports Export mode only.
        if (!string.Equals(job.Mode, "Export", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "TFS agent does not support mode {Mode} for job {JobId} — failing.",
                job.Mode, job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        // Build TFS-specific services for the export.
        // The TFS OM services are resolved from the host's DI container via the MigrationPlatformHost
        // registered by the composition root in TfsMigrationAgentServiceExtensions.
        // For now, use the WorkItemExportOrchestrator directly with the DI-provided sources.
        bool failed = false;
        try
        {
            _logger.LogInformation("Starting TFS export for job {JobId}.", job.JobId);

            // TODO: Resolve IWorkItemRevisionSource, IAttachmentBinarySource, IClassificationTreeReader
            // from DI once the TFS DI registrations are moved from CLI.TfsMigration to here.
            // For now, signal that TFS export is not yet fully wired.
            _logger.LogWarning(
                "TFS export agent DI wiring is pending — TFS-specific services need to be registered. Job {JobId}.",
                job.JobId);

            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TFS export failed for job {JobId}.", job.JobId);
            failed = true;
        }

        var terminal = failed ? "fail" : "complete";
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Discovery execution ───────────────────────────────────────────────────

    protected override async Task OnDiscoveryJobAsync(
        DiscoveryJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = _packageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        // TODO: Implement TFS discovery execution using IWorkItemDiscoveryService
        // once the DI registrations are moved from CLI.TfsMigration to here.
        _logger.LogWarning(
            "TFS discovery agent DI wiring is pending — TFS-specific services need to be registered. Job {JobId}.",
            job.JobId);

        await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
    }
}
