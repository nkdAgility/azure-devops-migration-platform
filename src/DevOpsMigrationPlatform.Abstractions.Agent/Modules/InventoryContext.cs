// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Context passed to <see cref="IModule.InventoryAsync(InventoryContext, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// A module operates against exactly one project per call.
/// The executor is responsible for supplying the correct <see cref="Project"/> and
/// <see cref="SourceEndpoint"/> for each task. The module must not loop over projects
/// or fall back to any other source of project name.
/// </remarks>
public sealed record InventoryContext
{
    public Job Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public IStateStore StateStore { get; init; } = null!;
    public IProgressSink? ProgressSink { get; init; }
    public IJobMetricsStore? MetricsStore { get; init; }
    public IJobSnapshotStore? SnapshotStore { get; init; }
    public OrganisationEndpoint SourceEndpoint { get; init; } = null!;

    /// <summary>
    /// The single project this inventory task targets.
    /// Always set by the executor from <c>JobTask.ProjectName</c>.
    /// Modules must process this project only — no loops, no discovery, no fallback.
    /// </summary>
    public string Project { get; init; } = string.Empty;
}

