using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
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
/// <see cref="OnMigrationJobAsync"/> completely and use the protected fields directly.
/// </summary>
public abstract class ModulePipelineWorkerBase : AgentWorkerBase
{
    /// <summary>Migration modules registered for this agent (ordered).</summary>
    protected IEnumerable<IModule> MigrationModules { get; }

    /// <summary>Factory for creating per-job artefact and state stores.</summary>
    protected IPackageStoreFactory PackageStoreFactory { get; }

    /// <summary>Progress sink for emitting <see cref="ProgressEvent"/> records.</summary>
    protected IProgressSink ProgressSink { get; }

    /// <summary>Factory for creating per-job checkpointing services.</summary>
    protected ICheckpointingServiceFactory CheckpointingFactory { get; }

    /// <summary>Factory for creating per-job phase-tracking services.</summary>
    protected IPhaseTrackingServiceFactory PhaseTrackingFactory { get; }

    /// <summary>Logger surfaced to the module pipeline (typed to the concrete subclass via DI).</summary>
    protected ILogger Logger { get; }

    protected ModulePipelineWorkerBase(
        IEnumerable<IModule> migrationModules,
        IPackageStoreFactory packageStoreFactory,
        IProgressSink progressSink,
        ICheckpointingServiceFactory checkpointingFactory,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IHttpClientFactory httpClientFactory,
        ILogger logger
#if !NET481
        , PolymorphicEndpointOptionsConverter? endpointConverter = null
#endif
        ) : base(leaseState, packageState, httpClientFactory, logger
#if !NET481
                 , endpointConverter
#endif
                 )
    {
        MigrationModules = migrationModules ?? throw new ArgumentNullException(nameof(migrationModules));
        PackageStoreFactory = packageStoreFactory ?? throw new ArgumentNullException(nameof(packageStoreFactory));
        ProgressSink = progressSink ?? throw new ArgumentNullException(nameof(progressSink));
        CheckpointingFactory = checkpointingFactory ?? throw new ArgumentNullException(nameof(checkpointingFactory));
        PhaseTrackingFactory = phaseTrackingFactory ?? throw new ArgumentNullException(nameof(phaseTrackingFactory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called inside the export try-block, immediately before the module loop.
    /// Override to set up connector-specific state (e.g. open a TFS connection,
    /// populate an ambient service holder).
    /// </summary>
    protected virtual Task OnBeforeModulesAsync(MigrationJob job, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Called in the finally-block that wraps the module loop.
    /// Override to tear down connector-specific state (e.g. clear ambient holders,
    /// dispose the TFS connection).
    /// </summary>
    protected virtual Task OnAfterModulesAsync(CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Default Export-only migration job implementation.
    /// Sets up stores and checkpointing, handles ForceFresh (deleting cursors for every
    /// registered module), invokes the connector setup hook, runs the module export loop,
    /// invokes the connector teardown hook, then signals a terminal state to the control plane.
    ///
    /// Override completely (and call connector hooks manually if needed) when the worker
    /// must support additional modes such as Both, Import, or Prepare.
    /// </summary>
    protected override async Task OnMigrationJobAsync(
        MigrationJob job, HttpClient controlPlane, string leaseId, CancellationToken ct)
    {
        var (artefactStore, stateStore) = PackageStoreFactory.Create(job.Package.PackageUri ?? ".");
        PackageState.CurrentStore = artefactStore;

        var checkpointer = CheckpointingFactory.Create(stateStore);

        if (job.Resume?.Mode == ResumeMode.ForceFresh)
        {
            Logger.LogInformation("ForceFresh requested for job {JobId} — deleting module cursors.", job.JobId);
            foreach (var module in MigrationModules)
            {
                await checkpointer.DeleteCursorAsync(module.Name, ct).ConfigureAwait(false);
                Logger.LogDebug("Deleted cursor for module {Module}.", module.Name);
            }
        }

        var exportContext = new ExportContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = ProgressSink
        };

        bool failed = false;
        try
        {
            await OnBeforeModulesAsync(job, ct).ConfigureAwait(false);

            foreach (var module in MigrationModules)
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
        }

        await SignalTerminalAsync(controlPlane, leaseId, failed ? "fail" : "complete", ct)
            .ConfigureAwait(false);
    }
}
