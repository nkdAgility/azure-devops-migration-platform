// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Minimal capture contract. Implementations perform a single org+project
/// discovery pass and write per-project artefacts via IArtefactStore.
/// <para>
/// <see cref="IModule"/> extends this interface, so all modules are capture
/// handlers. Pure <see cref="ICapture"/> implementations (not <see cref="IModule"/>)
/// are registered in DI as <c>ICapture</c> only and are always included in the
/// capture handler registry regardless of module phase flags.
/// </para>
/// </summary>
public interface ICapture
{
    /// <summary>
    /// Unique handler name. Must match the second dot-separated segment of the
    /// corresponding <c>capture.*</c> task ID.
    /// Example: <c>"workitems"</c> for task ID <c>capture.workitems.{org}.{project}</c>.
    /// Names are compared case-insensitively. Duplicate names cause a startup-time
    /// <see cref="System.ArgumentException"/> when the capture handler dictionary is built.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Captures data for a single org+project pair into IArtefactStore artefacts.
    /// The <paramref name="context"/> is scoped per-task by the plan executor:
    /// <c>context.SourceEndpoint</c> and <c>context.Project</c> are set to the
    /// specific org URL and project name for this task.
    /// Implementations MUST write their output via <c>context.ArtefactStore</c>.
    /// </summary>
    /// <param name="context">Per-task scoped inventory context.</param>
    /// <param name="ct">Cancellation token; propagate to all async operations.</param>
    Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct);
}
