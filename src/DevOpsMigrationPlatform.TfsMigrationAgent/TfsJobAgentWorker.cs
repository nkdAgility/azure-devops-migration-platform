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
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.TfsMigrationAgent;

/// <summary>
/// TFS-specific agent worker. Extends <see cref="ModulePipelineWorkerBase"/> to inherit
/// the connector-agnostic export pipeline (stores, checkpointing, ForceFresh, module loop).
/// Overrides <see cref="ModulePipelineWorkerBase.OnBeforeModulesAsync"/> to open a per-job
/// TFS Object Model connection and populate <see cref="ActiveTfsJobServices"/>, and
/// <see cref="ModulePipelineWorkerBase.OnAfterModulesAsync"/> to release that connection.
/// Advertises <c>Capabilities = ["tfs"]</c> so only TFS jobs are acquired.
/// </summary>
public sealed class TfsJobAgentWorker : ModulePipelineWorkerBase
{
    private readonly IEnumerable<IFlushable> _flushables;
    private readonly ITfsJobServiceFactory _tfsServiceFactory;
    private readonly ActiveTfsJobServices _activeTfsJobServices;
    private readonly ILogger<TfsJobAgentWorker> _logger;

    // Per-job TFS connection — set in OnBeforeModulesAsync, cleared in OnAfterModulesAsync.
    private TfsJobServices? _currentTfsServices;

    public TfsJobAgentWorker(
        IEnumerable<IModule> migrationModules,
        IPackageStoreFactory packageStoreFactory,
        IProgressSink progressSink,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        ActiveJobConfigState activeJobConfig,
        IPackageConfigStore packageConfigStore,
        IServiceScopeFactory moduleScopeFactory,
        IHttpClientFactory httpClientFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        IEnumerable<IFlushable> flushables,
        ITfsJobServiceFactory tfsServiceFactory,
        ActiveTfsJobServices activeTfsJobServices,
        ILogger<TfsJobAgentWorker> logger)
        : base(migrationModules, packageStoreFactory, progressSink, checkpointingFactory,
               phaseTrackingFactory, leaseState, packageState, activeJobConfig, packageConfigStore,
               moduleScopeFactory, httpClientFactory, logger)
    {
        _flushables = flushables;
        _tfsServiceFactory = tfsServiceFactory;
        _activeTfsJobServices = activeTfsJobServices;
        _logger = logger;
    }

    protected override ConnectorType[] Capabilities => new[] { ConnectorType.TeamFoundationServer };

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
                await OnExportJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            case JobKind.Inventory:
            case JobKind.Dependencies:
                await OnDiscoveryJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            default:
                _logger.LogError(
                    "TFS agent only supports Export, Inventory, and Dependencies — rejecting kind {JobKind} for job {JobId}.",
                    job.Kind, job.JobId);
                await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
                break;
        }
    }

    // ── Migration execution ───────────────────────────────────────────────────

    /// <summary>
    /// Validates that the job is Export kind, then delegates to the base export pipeline.
    /// </summary>
    private async Task OnExportJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        // Delegate the full export pipeline to the base class.
        // OnBeforeModulesAsync and OnAfterModulesAsync handle TFS connection setup/teardown.
        await base.OnJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override Task OnBeforeModulesAsync(Job job, CancellationToken ct)
    {
        // Bind the concrete TFS source endpoint from the raw IConfiguration stored in the
        // ambient state (IConfiguration.Bind cannot instantiate abstract MigrationEndpointOptions).
        var section = ActiveJobConfig.PackageConfig?.GetSection("MigrationPlatform:Source");
        TeamFoundationServerEndpointOptions? source = null;

        if (section != null && section.Exists() && !string.IsNullOrEmpty(section["Url"]))
            source = BindTfsSource(section);

        if (source == null || string.IsNullOrEmpty(source.Url))
            throw new InvalidOperationException(
                $"Job {job.JobId}: migration-config.json has no Source endpoint. Cannot establish TFS connection.");

        _logger.LogInformation(
            "Connecting to TFS for job {JobId} at {Url}/{Project}.",
            job.JobId, source.GetResolvedUrl(), source.GetProject());

        _currentTfsServices = _tfsServiceFactory.CreateForEndpoint(source);
        _activeTfsJobServices.Current = _currentTfsServices;
        return Task.CompletedTask;
    }

    private static TeamFoundationServerEndpointOptions BindTfsSource(IConfiguration section)
    {
        var opts = new TeamFoundationServerEndpointOptions();
        section.Bind(opts);
        return opts;
    }

    /// <inheritdoc/>
    protected override Task OnAfterModulesAsync(CancellationToken ct)
    {
        _activeTfsJobServices.Clear();
        _currentTfsServices?.Dispose();
        _currentTfsServices = null;
        return Task.CompletedTask;
    }

    // ── Discovery execution ───────────────────────────────────────────────────

    private async Task OnDiscoveryJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
            job.Package.PackageUri ?? ".");

        PackageState.CurrentStore = artefactStore;

        // Discovery config (organisations, endpoint) is read from migration-config.json.
        // Read the raw config section for the TFS source endpoint.
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
            return;
        }

        var section = packageConfig.GetSection("MigrationPlatform:Source");
        TeamFoundationServerEndpointOptions? endpointOptions = null;
        if (section != null && section.Exists() && !string.IsNullOrEmpty(section["Url"]))
        {
            endpointOptions = new TeamFoundationServerEndpointOptions();
            section.Bind(endpointOptions);
        }

        if (endpointOptions == null || string.IsNullOrEmpty(endpointOptions.Url))
        {
            _logger.LogError("Discovery job {JobId} has no TFS Source endpoint in migration-config.json — failing.", job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        bool failed = false;
        TfsJobServices? tfsServices = null;
        try
        {
            using var dataScope = DataClassificationScope.Begin(DataClassification.Customer);
            _logger.LogInformation(
                "Starting TFS discovery for job {JobId} against {Url}.",
                job.JobId, endpointOptions.GetResolvedUrl());

            tfsServices = _tfsServiceFactory.CreateForEndpoint(endpointOptions);

            var orgEndpoint = endpointOptions.ToOrganisationEndpoint();
            var project = endpointOptions.GetProject();

            ProgressSink.Emit(new ProgressEvent
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
                ProgressSink.Emit(new ProgressEvent
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
        // Flush buffered sinks before signalling — the CLI kills this process on receipt.
        foreach (var flushable in _flushables)
            await flushable.FlushAsync().ConfigureAwait(false);
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Classification tree capture ───────────────────────────────────────────
}
