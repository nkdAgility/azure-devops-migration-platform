// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Platform.Configuration;
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
    private readonly ICurrentJobEndpointAccessor _endpointAccessor;
    private readonly ILogger<TfsJobAgentWorker> _logger;
    private readonly IPackageAccess _package;

    // Per-job TFS connection — set in OnBeforeModulesAsync, cleared in OnAfterModulesAsync.
    private TfsJobServices? _currentTfsServices;

    // Package URI for the current job — set in OnBeforeModulesAsync, used in OnAfterModulesAsync.
    private string? _currentPackageUri;

    public TfsJobAgentWorker(
        IProgressSink progressSink,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IActiveJobState activeJobState,
        ICurrentPackageConfigAccessor currentPackageConfigAccessor,
        IPackageMigrationConfigLoader packageMigrationConfigLoader,
        IServiceScopeFactory moduleScopeFactory,
        IHttpClientFactory httpClientFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        IEnumerable<IFlushable> flushables,
        ITfsJobServiceFactory tfsServiceFactory,
        ActiveTfsJobServices activeTfsJobServices,
        ICurrentJobEndpointAccessor endpointAccessor,
        ILogger<TfsJobAgentWorker> logger,
        IPackageAccess? package,
        UnifiedWorkerEventWriter eventWriter)
        : base(progressSink, checkpointingFactory,
             phaseTrackingFactory, leaseState, packageState, currentPackageConfigAccessor, packageMigrationConfigLoader,
                package!, moduleScopeFactory, httpClientFactory, logger, eventWriter, activeJobState)
    {
        _flushables = flushables;
        _tfsServiceFactory = tfsServiceFactory;
        _activeTfsJobServices = activeTfsJobServices;
        _endpointAccessor = endpointAccessor;
        _logger = logger;
        _package = package ?? throw new ArgumentNullException(nameof(package));
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
        await InitializeJobPackageAsync(job, ct).ConfigureAwait(false);

        switch (job.Kind)
        {
            case JobKind.Export:
                await OnExportJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            case JobKind.Import:
                await OnImportJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            case JobKind.Inventory:
            case JobKind.Dependencies:
                await OnDiscoveryJobAsync(job, controlPlane, leaseId, ct).ConfigureAwait(false);
                break;

            default:
                _logger.LogError(
                    "TFS agent does not support job kind {JobKind} — rejecting job {JobId}.",
                    job.Kind, job.JobId);
                await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
                break;
        }
    }

    // ── Migration execution ───────────────────────────────────────────────────

    /// <summary>
    /// Handles <see cref="JobKind.Import"/> jobs: connects to the TFS/ADO Target endpoint,
    /// extracts any fixture package, builds the execution plan, and runs the import phase.
    /// </summary>
    private async Task OnImportJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        ActiveJobIdentity?.Set(job.JobId, job.Kind.ToString());

        // migration-config.json was already written by InitializeJobPackageAsync.
        IConfiguration packageConfig;
        try
        {
            packageConfig = await PackageMigrationConfigLoader.LoadAsync(ct).ConfigureAwait(false);
            CurrentPackageConfig.Set(packageConfig);
        }
        catch (PackageConfigNotFoundException ex)
        {
            _logger.LogError(ex,
                "Config file not found for import job {JobId} — failing.", job.JobId);
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            ActiveJobIdentity?.Clear();
            return;
        }

        // For import, the source is the package — no TFS Object Model source connection is needed.
        // But we DO need a TFS Object Model connection to the TARGET to create work items there.
        // Populate the endpoint accessor so ITargetEndpointInfo resolves correctly.
        SetImportEndpointContext(packageConfig);

        // Connect to the TFS TARGET endpoint so import modules can create work items.
        var targetEndpoint = TryBindTfsEndpoint(packageConfig, "MigrationPlatform:Target");
        if (targetEndpoint != null)
        {
            try
            {
                _currentTfsServices = _tfsServiceFactory.CreateForEndpoint(targetEndpoint);
                _activeTfsJobServices.Current = _currentTfsServices;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not connect to TFS target endpoint for import job {JobId}. " +
                    "Import will proceed without TFS OM connection (REST-only path).", job.JobId);
            }
        }

        bool failed = false;
        using var jobScope = ModuleScopeFactory.CreateScope();
        try
        {
            var jobModules = jobScope.ServiceProvider.GetServices<IModule>().ToList();
            var planBuilder = jobScope.ServiceProvider.GetRequiredService<IJobExecutionPlanBuilder>();
            var planExecutor = jobScope.ServiceProvider.GetRequiredService<IJobPlanExecutor>();

            // Extract fixture archive into package store if PackagePath is set.
            var preparer = jobScope.ServiceProvider.GetService<IPackagePreparer>();
            if (preparer != null)
                await preparer.PrepareForImportAsync(packageConfig, ct).ConfigureAwait(false);

            // Build (or resume) the execution plan and persist it to the package.
            var executionPlan = await planBuilder
                .BuildAndSaveAsync(packageConfig, job.Kind, PackageAccess, ct)
                .ConfigureAwait(false);

            // Push plan to the control plane for display (best-effort).
            var telemetry = jobScope.ServiceProvider.GetService<IControlPlaneTelemetryClient>();
            if (telemetry != null)
            {
                try
                {
                    await telemetry.PushTaskListAsync(leaseId, executionPlan, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to push task list for import job {JobId} — continuing.", job.JobId);
                }
            }

            var moduleMap = jobModules.ToDictionary(
                m => m.Name, m => (IModule)m, StringComparer.OrdinalIgnoreCase);
            var importContext = new ImportContext
            {
                Job = job,
                Package = PackageAccess,
                ProgressSink = ProgressSink
            };

            var importOk = await planExecutor.ImportAsync(
                executionPlan, moduleMap, importContext, ct).ConfigureAwait(false);

            failed = !importOk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed during module execution.", job.JobId);
            failed = true;
        }
        finally
        {
            _endpointAccessor.Clear();
            _activeTfsJobServices.Clear();
            _currentTfsServices?.Dispose();
            _currentTfsServices = null;
            CurrentPackageConfig.Clear();
            ActiveJobIdentity?.Clear();
        }

        await SignalTerminalAsync(controlPlane, leaseId, failed ? "fail" : "complete", ct)
            .ConfigureAwait(false);
    }

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
        // current per-job package configuration.
        var source = TryBindTfsSource(CurrentPackageConfig.Current);

        if (source == null || string.IsNullOrEmpty(source.Url))
            throw new InvalidOperationException(
                $"Job {job.JobId}: migration-config.json has no Source endpoint. Cannot establish TFS connection.");

        _logger.LogInformation(
            "Connecting to TFS for job {JobId} at {Url}/{Project}.",
            job.JobId, source.GetResolvedUrl(), source.GetProject());

        _currentTfsServices = _tfsServiceFactory.CreateForEndpoint(source);
        _activeTfsJobServices.Current = _currentTfsServices;
        _currentPackageUri = PackageState.CurrentPackageUri ?? ".";

        // Update plan file to mark TFS export task as Running (best-effort).
        await UpdatePlanTaskStatusAsync("export.workitems", JobTaskStatus.Running, ct)
            .ConfigureAwait(false);
    }

    private static TeamFoundationServerEndpointOptions BindTfsSource(IConfiguration section)
    {
        var opts = new TeamFoundationServerEndpointOptions();
        section.Bind(opts);
        return opts;
    }

    private static TeamFoundationServerEndpointOptions? TryBindTfsEndpoint(
        IConfiguration? packageConfig, string sectionPath)
    {
        var section = packageConfig?.GetSection(sectionPath);
        if (section == null || !section.Exists() || string.IsNullOrEmpty(section["Url"]))
            return null;

        return BindTfsSource(section);
    }

    private static TeamFoundationServerEndpointOptions? TryBindTfsSource(IConfiguration? packageConfig)
    {
        var section = packageConfig?.GetSection("MigrationPlatform:Source");
        if (section == null || !section.Exists() || string.IsNullOrEmpty(section["Url"]))
            return null;

        return BindTfsSource(section);
    }

    /// <summary>
    /// Sets the target endpoint in <see cref="ICurrentJobEndpointAccessor"/> from the
    /// import job's migration config.
    /// </summary>
    private void SetImportEndpointContext(IConfiguration packageConfig)
    {
        var url = ConfigTokenResolver.Resolve(packageConfig["MigrationPlatform:Target:Url"])?.Trim();
        var project = packageConfig["MigrationPlatform:Target:Project"]?.Trim();
        var connectorType = packageConfig["MigrationPlatform:Target:Type"]?.Trim();
        var accessToken = ConfigTokenResolver.Resolve(
            packageConfig["MigrationPlatform:Target:Authentication:AccessToken"]
            ?? packageConfig["MigrationPlatform:Target:Authentication:Token"])?.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(connectorType))
        {
            _endpointAccessor.ClearTarget();
            return;
        }

        _endpointAccessor.SetTarget(new ImportTargetEndpointInfo(
            url!,
            project ?? string.Empty,
            connectorType!,
            accessToken));
    }

    /// <summary>Inline target endpoint info for import jobs.</summary>
    private sealed record ImportTargetEndpointInfo(
        string Url,
        string Project,
        string ConnectorType,
        string? AccessToken) : ITargetEndpointInfo
    {
        public string OrganisationSlug => EndpointSlugHelper.ExtractSlug(Url);

        public OrganisationEndpoint ToOrganisationEndpoint() => new OrganisationEndpoint
        {
            ResolvedUrl = Url,
            Type = ConnectorType,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = AccessToken
            }
        };
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
            await UpdatePlanTaskStatusAsync("export.workitems", JobTaskStatus.Completed, ct)
                .ConfigureAwait(false);
            _currentPackageUri = null;
        }
    }

    /// <summary>
    /// Updates the task status in the persisted plan file.
    /// Best-effort — logs warnings on failure but does not throw.
    /// </summary>
    private async Task UpdatePlanTaskStatusAsync(
        string taskId,
        JobTaskStatus newStatus,
        CancellationToken ct)
    {
        try
        {
            string? json = null;
            if (_package is not null)
            {
                var result = await _package.RequestMetaAsync(
                    new PackageMetaContext(PackageMetaKind.ExecutionPlan),
                    ct).ConfigureAwait(false);
                if (result.Payload is not null)
                {
                    using var reader = new StreamReader(result.Payload.Content);
                    json = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                if (json is null)
                {
                    _logger.LogDebug("No execution plan payload returned through package boundary for {Path}.", result.ResolvedPath);
                }
            }

            if (json == null)
            {
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

            if (_package is null)
                throw new InvalidOperationException("IPackageAccess is required for TFS plan persistence.");

            using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(plan));
            await _package.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.ExecutionPlan),
                new PackageMetaPayload(stream, "application/json"),
                ct).ConfigureAwait(false);

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

        // Discovery config (organisations, endpoint) is read from migration-config.json.
        // Read the raw config section for the TFS source endpoint.
        IConfiguration packageConfig;
        try
        {
            packageConfig = await PackageMigrationConfigLoader.LoadAsync(ct).ConfigureAwait(false);
            CurrentPackageConfig.Set(packageConfig);
        }
        catch (PackageConfigNotFoundException ex)
        {
            _logger.LogError(ex,
                "Config file not found in {PackageUri}. Re-submit the job via CLI.",
                PackageState.CurrentPackageUri ?? "(unknown)");
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            return;
        }

        var endpointOptions = TryBindTfsSource(packageConfig);

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
            CurrentPackageConfig.Clear();
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
