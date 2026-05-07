// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
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

    // Package URI for the current job — set in OnBeforeModulesAsync, used in OnAfterModulesAsync.
    private string? _currentPackageUri;

    public TfsJobAgentWorker(
        IEnumerable<IModule> migrationModules,
        IPackageStoreFactory packageStoreFactory,
        IProgressSink progressSink,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IJobConfiguration activeJobConfig,
        IActiveJobState activeJobState,
        ICurrentPackageConfigAccessor currentPackageConfigAccessor,
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
             phaseTrackingFactory, leaseState, packageState, activeJobConfig, currentPackageConfigAccessor, packageConfigStore,
               moduleScopeFactory, httpClientFactory, logger, activeJobState)
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
        var (artefactStore, _) = PackageStoreFactory.Create(job.Package.PackageUri ?? ".");
        PackageState.CurrentStore = artefactStore;
        await WriteRunMetadataAsync(job, artefactStore, ct).ConfigureAwait(false);

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

    private async Task WriteRunMetadataAsync(Job job, IArtefactStore artefactStore, CancellationToken ct)
    {
        var runId = PackageState.CurrentRunId;
        if (string.IsNullOrEmpty(runId))
            return;

        var runJobPath = PackagePaths.RunJobFile(runId!);
        var jobJson = JsonSerializer.Serialize(job, AgentJsonOptions);
        await artefactStore.WriteAsync(runJobPath, jobJson, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(job.ConfigPayload))
        {
            var runConfigPath = PackagePaths.RunConfigFile(runId!);
            await artefactStore.WriteAsync(runConfigPath, job.ConfigPayload!, ct).ConfigureAwait(false);
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
    protected override async Task OnBeforeModulesAsync(Job job, CancellationToken ct)
    {
        // Bind the concrete TFS source endpoint from the raw IConfiguration stored in the
        // ambient state (IConfiguration.Bind cannot instantiate abstract MigrationEndpointOptions).
        var section = CurrentPackageConfig.Current?.GetSection("MigrationPlatform:Source");
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
        _currentPackageUri = job.Package.PackageUri ?? ".";

        // Update plan file to mark TFS export task as Running (best-effort).
        var (_, stateStore) = PackageStoreFactory.Create(_currentPackageUri);
        await UpdatePlanTaskStatusAsync(stateStore, "export.workitems", JobTaskStatus.Running, ct)
            .ConfigureAwait(false);
    }

    private static TeamFoundationServerEndpointOptions BindTfsSource(IConfiguration section)
    {
        var opts = new TeamFoundationServerEndpointOptions();
        section.Bind(opts);
        return opts;
    }

    /// <inheritdoc/>
    protected override async Task OnAfterModulesAsync(CancellationToken ct)
    {
        _activeTfsJobServices.Clear();
        _currentTfsServices?.Dispose();
        _currentTfsServices = null;

        // Update plan file to mark TFS export task as Completed (best-effort).
        // If the job failed, the status remains Failed (set in the exception handler).
        if (_currentPackageUri != null)
        {
            var (_, stateStore) = PackageStoreFactory.Create(_currentPackageUri);
            await UpdatePlanTaskStatusAsync(stateStore, "export.workitems", JobTaskStatus.Completed, ct)
                .ConfigureAwait(false);
            _currentPackageUri = null;
        }
    }

    /// <summary>
    /// Updates the task status in the persisted plan file.
    /// Best-effort — logs warnings on failure but does not throw.
    /// </summary>
    private async Task UpdatePlanTaskStatusAsync(
        IStateStore stateStore,
        string taskId,
        JobTaskStatus newStatus,
        CancellationToken ct)
    {
        try
        {
            var json = await stateStore.ReadAsync(PackagePaths.PlanFile, ct).ConfigureAwait(false);
            if (json == null)
            {
                _logger.LogDebug("No plan file found at {Path} — skipping TFS task status update.", PackagePaths.PlanFile);
                return;
            }

            var plan = JsonSerializer.Deserialize<JobTaskList>(json);
            if (plan == null)
                return;

            var taskList = plan.Tasks.ToList();
            // Task IDs now include org/project slugs (e.g. export.workitems.myorg.projecta),
            // so match by prefix to remain compatible with the updated task ID format.
            var idx = taskList.FindIndex(t =>
                t.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase) ||
                t.Id.StartsWith(taskId + ".", StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                _logger.LogDebug("Task {TaskId} not found in plan — skipping status update.", taskId);
                return;
            }

            var updated = taskList[idx] with
            {
                Status = newStatus,
                StartedAt = newStatus == JobTaskStatus.Running ? DateTimeOffset.UtcNow : taskList[idx].StartedAt,
                CompletedAt = newStatus == JobTaskStatus.Completed || newStatus == JobTaskStatus.Failed
                    ? DateTimeOffset.UtcNow
                    : taskList[idx].CompletedAt
            };

            taskList[idx] = updated;
            plan = plan with { Tasks = taskList.AsReadOnly() };

            var updatedJson = JsonSerializer.Serialize(plan);
            await stateStore.WriteAsync(PackagePaths.PlanFile, updatedJson, ct).ConfigureAwait(false);

            _logger.LogDebug("Updated TFS task {TaskId} to {Status} in plan file.", taskId, newStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update TFS task status in plan file — job will continue.");
        }
    }

    // ── Discovery execution ───────────────────────────────────────────────────

    private async Task OnDiscoveryJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        ActiveJobIdentity?.Set(job.JobId, job.Kind.ToString());
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
            ActiveJobIdentity?.Clear();
        }

        var terminal = failed ? "fail" : "complete";
        // Flush buffered sinks before signalling — the CLI kills this process on receipt.
        foreach (var flushable in _flushables)
            await flushable.FlushAsync().ConfigureAwait(false);
        await SignalTerminalAsync(controlPlane, leaseId, terminal, ct).ConfigureAwait(false);
    }

    // ── Classification tree capture ───────────────────────────────────────────
}
