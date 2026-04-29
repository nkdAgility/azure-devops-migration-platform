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
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using Microsoft.Extensions.Configuration;
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
    private readonly PackageProgressSink _packageProgressSink;
    private readonly PackageLoggerProvider _packageLoggerProvider;
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
        IHttpClientFactory httpClientFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        PackageProgressSink packageProgressSink,
        PackageLoggerProvider packageLoggerProvider,
        ITfsJobServiceFactory tfsServiceFactory,
        ActiveTfsJobServices activeTfsJobServices,
        ILogger<TfsJobAgentWorker> logger)
        : base(migrationModules, packageStoreFactory, progressSink, checkpointingFactory,
               phaseTrackingFactory, leaseState, packageState, activeJobConfig, packageConfigStore,
               httpClientFactory, logger)
    {
        _packageProgressSink = packageProgressSink;
        _packageLoggerProvider = packageLoggerProvider;
        _tfsServiceFactory = tfsServiceFactory;
        _activeTfsJobServices = activeTfsJobServices;
        _logger = logger;
    }

    protected override string[] Capabilities => new[] { "tfs" };

    protected override async Task OnPostJobFlushAsync()
    {
        await _packageProgressSink.FlushAsync().ConfigureAwait(false);
        await _packageLoggerProvider.FlushAsync().ConfigureAwait(false);
    }

    // ── Migration execution ───────────────────────────────────────────────────

    /// <summary>
    /// Validates that the job is Export mode, then delegates to the base export pipeline.
    /// </summary>
    protected override async Task OnMigrationJobAsync(
        MigrationJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        // TFS agent supports Export mode only.
        if (!string.Equals(job.Mode, "Export", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "TFS agent only supports Export mode — rejecting mode {Mode} for job {JobId}.",
                job.Mode, job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        // Delegate the full export pipeline to the base class.
        // OnBeforeModulesAsync and OnAfterModulesAsync handle TFS connection setup/teardown.
        await base.OnMigrationJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override Task OnBeforeModulesAsync(MigrationJob job, CancellationToken ct)
    {
        // Bind the concrete TFS source endpoint from the raw IConfiguration stored in the
        // ambient state (IConfiguration.Bind cannot instantiate abstract MigrationEndpointOptions).
        var section = ActiveJobConfig.PackageConfig?.GetSection("MigrationPlatform:Source");
        TeamFoundationServerEndpointOptions? source = null;

        if (section != null && section.Exists() && !string.IsNullOrEmpty(section["Url"]))
            source = BindTfsSource(section);

        source ??= (ActiveJobConfig.Current?.Source as TeamFoundationServerEndpointOptions);

        if (source == null || string.IsNullOrEmpty(source.Url))
            throw new InvalidOperationException(
                $"Job {job.JobId}: migration-config.json has no Source endpoint. Cannot establish TFS connection.");

        // Make the concrete source available on the ambient MigrationOptions so modules
        // that read _activeJobConfig?.Current?.Source get the correct endpoint.
        if (ActiveJobConfig.Current != null)
            ActiveJobConfig.Current.Source = source;

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

    protected override async Task OnDiscoveryJobAsync(
        DiscoveryJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(
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
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Classification tree capture ───────────────────────────────────────────
}
