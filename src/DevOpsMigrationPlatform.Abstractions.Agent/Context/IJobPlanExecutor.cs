// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Executes a <see cref="JobTaskList"/> using a topological tier sort,
/// running independent tasks in parallel via Task.WhenAll and persisting task
/// status to the package after every transition. Circular dependencies throw
/// before any task executes.
/// </summary>
/// <remarks>
/// Dispatch is driven by <see cref="JobTask.TaskKind"/>, not by <c>Phase</c>.
/// Use <see cref="ExecuteTasksAsync"/> for all job kinds. The phase-specific
/// methods are kept for backward compatibility and delegate internally.
/// </remarks>
public interface IJobPlanExecutor
{
    /// <summary>
    /// Executes all pending tasks in <paramref name="plan"/>, dispatching on
    /// <see cref="JobTask.TaskKind"/>. This is the unified execution path for all
    /// job kinds (Inventory, Export, Import, Migrate).
    /// Tasks run in dependency-tier order; independent tasks within a tier run concurrently.
    /// Returns <c>true</c> if all executed tasks succeeded; <c>false</c> if any failed.
    /// </summary>
    /// <param name="plan">The full execution plan.</param>
    /// <param name="captureHandlersByName">Capture handler instances keyed by <see cref="ICapture.Name"/> (case-insensitive).
    /// Includes all <see cref="IModule"/> instances where <see cref="IModule.SupportsInventory"/> is true,
    /// plus pure <see cref="ICapture"/> registrations (e.g. <c>DependencyCapture</c>).</param>
    /// <param name="analysersByName">Analyser instances keyed by <see cref="IAnalyser.Name"/> (case-insensitive).</param>
    /// <param name="baseInventoryContext">Base context for <see cref="TaskKind.Capture"/> tasks. The executor scopes
    /// <c>Project</c> and <c>SourceEndpoint</c> per task from <paramref name="endpointsByUrl"/>.</param>
    /// <param name="baseExportContext">Base context for <see cref="TaskKind.Export"/> tasks. The executor scopes
    /// <c>Project</c> per task.</param>
    /// <param name="importContext">Context for <see cref="TaskKind.Import"/> tasks (passed as-is).</param>
    /// <param name="endpointsByUrl">Map of organisation URL → endpoint, used to scope Capture tasks.</param>
    /// <param name="stateStore">State store for persisting the plan after each task transition.</param>
    /// <param name="ct">Cancellation token propagated to all running tasks.</param>
    Task<bool> ExecuteTasksAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, ICapture> captureHandlersByName,
        IReadOnlyDictionary<string, IAnalyser> analysersByName,
        InventoryContext? baseInventoryContext,
        ExportContext? baseExportContext,
        ImportContext? importContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
                CancellationToken ct);

    /// <summary>
    /// Executes Export-phase tasks in <paramref name="plan"/>.
    /// Returns <c>true</c> if all executed tasks succeeded; <c>false</c> if any failed.
    /// </summary>
    Task<bool> ExecuteExportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        IReadOnlyDictionary<string, IAnalyser> analysersByName,
        InventoryContext? baseInventoryContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
        ExportContext exportContext,
                CancellationToken ct);

    /// <summary>
    /// Executes Import-phase tasks in dependency-tier order.
    /// Returns <c>true</c> if all executed tasks succeeded; <c>false</c> if any failed.
    /// </summary>
    Task<bool> ExecuteImportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ImportContext importContext,
                CancellationToken ct);
}
