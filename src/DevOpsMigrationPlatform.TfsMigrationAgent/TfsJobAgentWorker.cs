using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.TfsMigrationAgent;

/// <summary>
/// TFS-specific agent worker. Inherits the polling loop and lease protocol from
/// <see cref="AgentWorkerBase"/>; implements TFS export via <see cref="WorkItemExportOrchestrator"/>
/// and TFS discovery via <see cref="IWorkItemDiscoveryService"/> obtained from
/// <see cref="TfsJobServiceFactory"/>.
/// Advertises <c>Capabilities = ["tfs"]</c> so only TFS jobs are acquired.
///
/// Uses the same <see cref="WorkItemExportOrchestrator"/>, checkpointing, and progress sinks
/// as the MigrationAgent — the only difference is the connector (TFS Object Model) and
/// per-job service creation via <see cref="TfsJobServiceFactory"/>.
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
    private readonly TfsJobServiceFactory _tfsServiceFactory;
    private readonly ILogger<TfsJobAgentWorker> _logger;

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_treeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

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
        TfsJobServiceFactory tfsServiceFactory,
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
        _tfsServiceFactory = tfsServiceFactory;
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
        var source = job.Source;
        if (source == null)
        {
            _logger.LogError("Job {JobId} has no Source endpoint — failing.", job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        // TFS agent supports Export mode only.
        if (!string.Equals(job.Mode, "Export", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "TFS agent does not support mode {Mode} for job {JobId} — failing.",
                job.Mode, job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        var (artefactStore, stateStore) = _packageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        var checkpointer = _checkpointingFactory.Create(stateStore);

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            _logger.LogInformation("ForceFresh requested for TFS job {JobId} — deleting cursors.", job.JobId);
            await checkpointer.DeleteCursorAsync("WorkItems", ct).ConfigureAwait(false);
        }

        bool failed = false;
        TfsJobServices? tfsServices = null;
        try
        {
            using var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);
            _logger.LogInformation(
                "Starting TFS export for job {JobId} against {Url}/{Project}.",
                job.JobId, source.GetResolvedUrl(), source.GetProject());

            // Create per-job TFS OM services from the job's source endpoint.
            tfsServices = _tfsServiceFactory.CreateForEndpoint(source);

            using var rootActivity = ActivitySource.StartActivity("TfsExport", ActivityKind.Server);
            rootActivity?.SetTag("job.id", job.JobId);
            rootActivity?.SetTag("tfs.project", source.GetProject());

            // Capture classification tree (area + iteration nodes) before work item export.
            await CaptureClassificationTreeAsync(
                tfsServices.ClassificationTreeReader, source, artefactStore, ct).ConfigureAwait(false);

            _progressSink.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Starting",
                Message = "Connecting to TFS and preparing export…",
                Timestamp = DateTimeOffset.UtcNow
            });

            // Use the same WorkItemExportOrchestrator as the MigrationAgent.
            var orchestrator = new WorkItemExportOrchestrator(
                artefactStore,
                checkpointer,
                attachmentBinarySource: tfsServices.AttachmentSource,
                progressSink: _progressSink,
                endpoint: source,
                project: source.GetProject(),
                discoveryService: tfsServices.DiscoveryService,
                jobId: job.JobId,
                logger: _logger);

            await orchestrator.ExportAsync(tfsServices.RevisionSource, ct).ConfigureAwait(false);

            _logger.LogInformation("TFS export completed for job {JobId}.", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TFS export failed for job {JobId}.", job.JobId);
            failed = true;
        }
        finally
        {
            tfsServices?.Dispose();
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

        // Discovery jobs carry the endpoint in Source or in Organisations.
        var endpointOptions = job.Source;
        if (endpointOptions == null && job.Organisations.Count > 0)
        {
            _logger.LogWarning(
                "TFS discovery job {JobId} has no Source — falling back to Organisations[0].", job.JobId);
            // For discovery, we use IWorkItemDiscoveryService which works with OrganisationEndpoint,
            // not the per-job factory. Signal not supported for now.
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        if (endpointOptions == null)
        {
            _logger.LogError("Discovery job {JobId} has no endpoint — failing.", job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        bool failed = false;
        TfsJobServices? tfsServices = null;
        try
        {
            using var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);
            _logger.LogInformation(
                "Starting TFS discovery for job {JobId} against {Url}.",
                job.JobId, endpointOptions.GetResolvedUrl());

            tfsServices = _tfsServiceFactory.CreateForEndpoint(endpointOptions);

            var orgEndpoint = endpointOptions.ToOrganisationEndpoint();
            var project = endpointOptions.GetProject();

            _progressSink.Emit(new ProgressEvent
            {
                Module = "Discovery",
                Stage = "Starting",
                Message = "Connecting to TFS for discovery…",
                Timestamp = DateTimeOffset.UtcNow
            });

            await foreach (var summary in tfsServices.DiscoveryService
                .CountWorkItemsAsync(orgEndpoint, project, cancellationToken: ct)
                .ConfigureAwait(false))
            {
                _progressSink.Emit(new ProgressEvent
                {
                    Module = "Discovery",
                    Stage = summary.IsWorkItemComplete ? "Completed" : "Progress",
                    Message = $"{project}: {summary.WorkItemsCount} work items, {summary.RevisionsCount} revisions",
                    Timestamp = DateTimeOffset.UtcNow
                });
            }

            _logger.LogInformation("TFS discovery completed for job {JobId}.", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TFS discovery failed for job {JobId}.", job.JobId);
            failed = true;
        }
        finally
        {
            tfsServices?.Dispose();
        }

        var terminal = failed ? "fail" : "complete";
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Classification tree capture ───────────────────────────────────────────

    /// <summary>
    /// Captures the full classification tree from TFS and writes Nodes/source-tree.json.
    /// Same logic as the CLI.TfsMigration ExportCommand — mirrors the ClassificationTreeCapture
    /// used by the ADO agent path.
    /// </summary>
    private static async Task CaptureClassificationTreeAsync(
        IClassificationTreeReader reader,
        MigrationEndpointOptions endpoint,
        IArtefactStore artefactStore,
        CancellationToken ct)
    {
        var areaNodes = new List<string>();
        var iterationNodes = new List<IterationNodeEntry>();

        await foreach (var path in reader.EnumerateAreaNodesAsync(endpoint, ct).ConfigureAwait(false))
            areaNodes.Add(path);

        await foreach (var entry in reader.EnumerateIterationNodesAsync(endpoint, ct).ConfigureAwait(false))
            iterationNodes.Add(entry);

        var snapshot = new { areaNodes, iterationNodes };
        var json = JsonSerializer.Serialize(snapshot, s_treeJsonOptions);
        await artefactStore.WriteAsync("Nodes/source-tree.json", json, ct).ConfigureAwait(false);
    }
}
