// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Context passed to IModule.ExportAsync.
/// Provides the job definition and all required stores — modules must not access
/// the filesystem, source system, or target system directly.
/// </summary>
public class ExportContext
{
    /// <summary>The job definition.</summary>
    public Job Job { get; init; } = null!;

    /// <summary>Artefact store for writing export output (WorkItems/, Nodes/, etc.).</summary>
    public IArtefactStore ArtefactStore { get; init; } = null!;

    /// <summary>State store for cursor-based checkpointing. Persists progress across restarts.</summary>
    public IStateStore StateStore { get; init; } = null!;

    /// <summary>Progress event sink. Emits structured events to the control plane ring buffer and package log.</summary>
    public IProgressSink ProgressSink { get; init; } = null!;

    /// <summary>
    /// Optional metrics store. When provided, modules push aggregate
    /// <see cref="JobMetrics"/> so the Control Plane telemetry snapshot (Channel 2)
    /// carries real-time counters. Null when running inside the TFS subprocess or unit tests.
    /// </summary>
    public IJobMetricsStore? MetricsStore { get; init; }

    /// <summary>
    /// Optional snapshot store (Channel 3). When provided, modules push
    /// <see cref="JobSnapshot"/> with per-org/project state for late-joining clients.
    /// Null when running inside the TFS subprocess or unit tests.
    /// </summary>
    public IJobSnapshotStore? SnapshotStore { get; init; }

    /// <summary>
    /// Organisations scoped to this job. Populated by the agent from <c>migration-config.json</c>
    /// in the package. Modules must read from this property rather than injecting
    /// <c>IOptions&lt;MigrationPlatformOptions&gt;</c> directly, so that per-job config is used.
    /// Empty list when running in unit tests without a package.
    /// </summary>
    public IReadOnlyList<ScopedOrganisationEndpoint> Organisations { get; init; } = [];

    /// <summary>
    /// The single project this export task targets.
    /// Set by the executor from <c>JobTask.ProjectName</c>.
    /// Modules should prefer this over <c>ISourceEndpointInfo.Project</c>.
    /// </summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>
    /// The current export task identifier from the execution plan.
    /// Modules may include this on progress events when they need the Control Plane
    /// to merge mid-task <c>CompletedCount</c> updates into the task model.
    /// </summary>
    public string? TaskId { get; init; }
}

