using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Executes a <see cref="JobTaskList"/> phase using a topological tier sort,
/// running independent tasks in parallel via Task.WhenAll and persisting task
/// status to the package after every transition. Circular dependencies throw
/// before any task executes.
/// </summary>
/// <remarks>
/// The plan executor replaces the hardcoded sequential `foreach` loops in
/// <c>JobAgentWorker</c> with dependency-aware parallel execution.
/// Inventory runs first when present (tier 0), followed by other Export tasks
/// that may depend on it (tier 1+). Import tasks are sorted into tiers based
/// on <see cref="IModule.DependsOn"/> and executed tier-by-tier.
/// </remarks>
public interface IJobPlanExecutor
{
    /// <summary>
    /// Executes all Export-phase tasks in <paramref name="plan"/> (including Inventory if present).
    /// Inventory has no dependencies and runs first. Other Export tasks may depend on Inventory.
    /// Tasks with no dependencies run concurrently in the same tier.
    /// Returns <c>true</c> if all executed tasks succeeded; <c>false</c> if any failed.
    /// </summary>
    /// <param name="plan">The execution plan containing Export-phase tasks (including Inventory).</param>
    /// <param name="modulesByName">Module instances keyed by <see cref="IModule.Name"/> (case-insensitive).</param>
    /// <param name="exportContext">Shared export context passed to <see cref="IModule.ExportAsync"/>.</param>
    /// <param name="stateStore">State store for persisting the plan after each task transition.</param>
    /// <param name="ct">Cancellation token propagated to all running tasks.</param>
    /// <returns><c>true</c> if no task failed; <c>false</c> otherwise.</returns>
    Task<bool> ExecuteExportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ExportContext exportContext,
        IStateStore stateStore,
        CancellationToken ct);

    /// <summary>
    /// Executes Import-phase tasks in dependency-tier order, running tasks
    /// within each tier concurrently via Task.WhenAll. Tasks that depend on
    /// a failed or skipped task are automatically skipped.
    /// Returns <c>true</c> if all executed tasks succeeded; <c>false</c> if any failed.
    /// </summary>
    /// <param name="plan">The execution plan containing Import-phase tasks.</param>
    /// <param name="modulesByName">Module instances keyed by <see cref="IModule.Name"/> (case-insensitive).</param>
    /// <param name="importContext">Shared import context passed to <see cref="IModule.ImportAsync"/>.</param>
    /// <param name="stateStore">State store for persisting the plan after each task transition.</param>
    /// <param name="ct">Cancellation token propagated to all running tasks.</param>
    /// <returns><c>true</c> if no task failed; <c>false</c> otherwise.</returns>
    Task<bool> ExecuteImportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ImportContext importContext,
        IStateStore stateStore,
        CancellationToken ct);
}
