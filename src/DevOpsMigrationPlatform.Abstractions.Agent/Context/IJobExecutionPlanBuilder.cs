using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Builds the execution plan (<see cref="JobTaskList"/>) for a job at the point
/// the agent acquires the lease and reads the package configuration.
/// Implementations inspect enabled modules, phase records, and available artefacts
/// to produce an ordered task list with pre-known totals where available.
/// </summary>
public interface IJobExecutionPlanBuilder
{
    /// <summary>
    /// Builds the task list for <paramref name="job"/> using the supplied package stores
    /// to check for skip conditions (e.g. inventory exists, phase already completed).
    /// </summary>
    /// <param name="packageConfig">Per-job configuration read from <c>migration-config.json</c>.</param>
    /// <param name="kind">The job kind (<see cref="JobKind"/>); determines which phases appear.</param>
    /// <param name="artefactStore">Package artefact store for the current job.</param>
    /// <param name="stateStore">Package state store for checkpointing / phase records.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ordered <see cref="JobTaskList"/> ready to be pushed to the Control Plane.</returns>
    Task<JobTaskList> BuildPlanAsync(
        IConfiguration packageConfig,
        JobKind kind,
        IArtefactStore artefactStore,
        IStateStore stateStore,
        CancellationToken ct);
}
