// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Serialization;
#endif

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Intermediate base class for agent workers that execute an <see cref="IModule"/> pipeline.
/// Lifts the connector-agnostic export pipeline (store setup, checkpointing, ForceFresh,
/// ExportContext construction, module loop, terminal signalling) out of individual workers.
///
/// Connector-specific setup and teardown (e.g. TFS Object Model connection, ADO credential
/// resolution) is handled via the <see cref="OnBeforeModulesAsync"/> and
/// <see cref="OnAfterModulesAsync"/> template-method hooks.
///
/// Workers that support additional migration modes (Both, Import, Prepare) should override
/// <see cref="AgentWorkerBase.OnJobAsync"/> completely and use the protected fields directly.
/// </summary>
public abstract class ModulePipelineWorkerBase : AgentWorkerBase
{
    /// <summary>Progress sink for emitting <see cref="ProgressEvent"/> records.</summary>
    protected IProgressSink ProgressSink { get; }

    /// <summary>Factory for creating per-job checkpointing services.</summary>
    protected ICheckpointingServiceFactory CheckpointingFactory { get; }

    /// <summary>Factory for creating per-job phase-tracking services.</summary>
    protected IPhaseTrackingServiceFactory PhaseTrackingFactory { get; }

    /// <summary>Logger surfaced to the module pipeline (typed to the concrete subclass via DI).</summary>
    protected ILogger Logger { get; }

    /// <summary>Reads <c>migration-config.json</c> from the package at job start.</summary>
    protected IPackageMigrationConfigLoader PackageMigrationConfigLoader { get; }

    /// <summary>Package boundary for package-scoped metadata and content writes.</summary>
    protected IPackageAccess PackageAccess { get; }

    /// <summary>Explicit holder for the current job's raw package configuration.</summary>
    protected ICurrentPackageConfigAccessor CurrentPackageConfig { get; }

    /// <summary>Ambient holder for the identity (JobId, Kind) of the currently executing job.</summary>
    private readonly IActiveJobState? _activeJobState;

    /// <summary>Exposes the active job state to subclasses that override <see cref="AgentWorkerBase.OnJobAsync"/> completely.</summary>
    protected IActiveJobState? ActiveJobIdentity => _activeJobState;

    /// <summary>
    /// Used to create a per-job DI scope so that modules (and their tool dependencies)
    /// are resolved AFTER the current package configuration is published from
    /// <c>migration-config.json</c>. This ensures Singleton tools whose
    /// <c>IOptions&lt;T&gt;.Value</c> is read at construction time receive the per-job config.
    /// Exposed as protected so subclasses that implement additional job kinds (e.g. Import)
    /// can resolve scoped services such as <c>IJobPlanExecutor</c>.
    /// </summary>
    protected IServiceScopeFactory ModuleScopeFactory { get; }

    protected ModulePipelineWorkerBase(
        IProgressSink progressSink,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        ICurrentPackageConfigAccessor currentPackageConfigAccessor,
        IPackageMigrationConfigLoader packageMigrationConfigLoader,
        IPackageAccess packageAccess,
        IServiceScopeFactory moduleScopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        IActiveJobState? activeJobState = null
#if !NET481
        , PolymorphicEndpointOptionsConverter? endpointConverter = null
        , PolymorphicOrganisationEntryConverter? organisationConverter = null
#endif
        ) : base(leaseState, packageState, httpClientFactory, logger
#if !NET481
                 , endpointConverter
                 , organisationConverter
#endif
                 )
    {
        ProgressSink = progressSink ?? throw new ArgumentNullException(nameof(progressSink));
        CheckpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        PhaseTrackingFactory = phaseTrackingFactory ?? throw new ArgumentNullException(nameof(phaseTrackingFactory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        PackageMigrationConfigLoader = packageMigrationConfigLoader ?? throw new ArgumentNullException(nameof(packageMigrationConfigLoader));
        PackageAccess = packageAccess ?? throw new ArgumentNullException(nameof(packageAccess));
        CurrentPackageConfig = currentPackageConfigAccessor ?? throw new ArgumentNullException(nameof(currentPackageConfigAccessor));
        _activeJobState = activeJobState;
        ModuleScopeFactory = moduleScopeFactory ?? throw new ArgumentNullException(nameof(moduleScopeFactory));
    }

    /// <summary>
    /// Called inside the export try-block, immediately before the module loop.
    /// Override to set up connector-specific state (e.g. open a TFS connection,
    /// populate an ambient service holder).
    /// </summary>
    protected virtual Task OnBeforeModulesAsync(Job job, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Called in the finally-block that wraps the module loop.
    /// Override to tear down connector-specific state (e.g. clear ambient holders,
    /// dispose the TFS connection).
    /// </summary>
    protected virtual Task OnAfterModulesAsync(CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Initializes package-scoped runtime state and writes run metadata into the package when a run id is active.
    /// </summary>
    protected async Task InitializeJobPackageAsync(
        Job job,
        CancellationToken ct)
    {
        await WriteRunMetadataAsync(job, ct).ConfigureAwait(false);
        await WriteConfigPayloadIfAbsentAsync(job, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes <c>migration-config.json</c> from <see cref="Job.ConfigPayload"/> when the file
    /// is not already present in the package (or when ForceFresh is set).
    /// <para>
    /// This ensures all agent workers (including the TFS agent, which does not carry its own
    /// config-write step) can reliably read <c>migration-config.json</c> at job startup.
    /// On resume runs (file exists, no ForceFresh), writing is skipped so that
    /// <see cref="DevOpsMigrationPlatform.MigrationAgent.JobAgentWorker"/> can perform its
    /// source/target compatibility validation before overwriting.
    /// </para>
    /// </summary>
    private async Task WriteConfigPayloadIfAbsentAsync(Job job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.ConfigPayload))
            return;

        var forceFresh = job.Resume?.Mode == ResumeMode.ForceFresh;
        var existing = await PackageAccess.RequestMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig), ct).ConfigureAwait(false);

        if (existing.Payload is not null && !forceFresh)
            return;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(job.ConfigPayload!), writable: false);
        await PackageAccess.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig),
            new PackageMetaPayload(stream),
            ct).ConfigureAwait(false);
    }

    protected async Task WriteRunMetadataAsync(Job job, CancellationToken ct)
    {
        var runId = PackageState.CurrentRunId;
        if (string.IsNullOrEmpty(runId))
            return;

        var jobJson = JsonSerializer.Serialize(job, AgentJsonOptions);
        using var jobStream = new MemoryStream(Encoding.UTF8.GetBytes(jobJson), writable: false);
        await PackageAccess.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress($".migration/runs/{runId}/job.json")),
            new PackagePayload(jobStream, "application/json"),
            ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(job.ConfigPayload))
        {
            using var cfgStream = new MemoryStream(Encoding.UTF8.GetBytes(job.ConfigPayload!), writable: false);
            await PackageAccess.PersistContentAsync(
                new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress($".migration/runs/{runId}/config.json")),
                new PackagePayload(cfgStream, "application/json"),
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Default Export-only job implementation.
    /// Sets up stores and checkpointing, handles ForceFresh (deleting cursors for every
    /// registered module), invokes the connector setup hook, runs the module export loop,
    /// invokes the connector teardown hook, then signals a terminal state to the control plane.
    ///
    /// Override completely (and call connector hooks manually if needed) when the worker
    /// must support additional modes such as Migrate, Import, or Prepare.
    /// </summary>
    protected override async Task OnJobAsync(
        Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        _activeJobState?.Set(job.JobId, job.Kind.ToString());
        await InitializeJobPackageAsync(job, ct).ConfigureAwait(false);

        // T035 — explicit fail-fast for pre-025 packages that have no migration-config.json.
        IConfiguration packageConfig;
        try
        {
            packageConfig = await PackageMigrationConfigLoader.LoadAsync(ct).ConfigureAwait(false);
        }
        catch (PackageConfigNotFoundException ex)
        {
            Logger.LogError(ex,
                "Config file not found: {PackageUri}. Re-submit the job via CLI.",
                PackageState.CurrentPackageUri ?? "(unknown)");
            await SignalTerminalAsync(controlPlane, leaseId, "fail", ct).ConfigureAwait(false);
            CurrentPackageConfig.Clear();
            return;
        }
        // Store the raw IConfiguration for per-job tool options binding.
        CurrentPackageConfig.Set(packageConfig);

        // Create a per-job DI scope AFTER PackageConfig is set. Modules (and their Singleton
        // tool dependencies like IFieldTransformTool) that read IOptions<T>.Value at
        // construction time will now receive values from migration-config.json rather than
        // the empty appsettings.json loaded at host startup.
        using var jobScope = ModuleScopeFactory.CreateScope();
        var jobModules = jobScope.ServiceProvider.GetServices<IModule>();

        var checkpointer = CheckpointingFactory.Create(PackageAccess);

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            Logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in jobModules)
            {
                await checkpointer.DeleteCursorAsync(module.Name, ct).ConfigureAwait(false);
                Logger.LogDebug("Deleted cursor for module {Module}.", module.Name);
            }
        }

        var exportContext = new ExportContext
        {
            Job = job,
            Package = PackageAccess,
            ProgressSink = ProgressSink
        };

        bool failed = false;
        try
        {
            await OnBeforeModulesAsync(job, ct).ConfigureAwait(false);

            foreach (var module in jobModules)
            {
                Logger.LogInformation("Running module {Module}.ExportAsync", module.Name);
                await module.ExportAsync(exportContext, ct).ConfigureAwait(false);
            }

            Logger.LogInformation("Module pipeline completed for job {JobId}.", job.JobId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Job {JobId} failed during module execution.", job.JobId);
            failed = true;
        }
        finally
        {
            await OnAfterModulesAsync(ct).ConfigureAwait(false);
            CurrentPackageConfig.Clear();
            _activeJobState?.Clear();
        }

        await SignalTerminalAsync(controlPlane, leaseId, failed ? "fail" : "complete", ct)
            .ConfigureAwait(false);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
