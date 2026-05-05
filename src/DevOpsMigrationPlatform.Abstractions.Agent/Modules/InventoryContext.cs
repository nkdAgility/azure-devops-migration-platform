// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Context passed to <see cref="IModule.InventoryAsync(InventoryContext, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed record InventoryContext
{
    public Job Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public IStateStore StateStore { get; init; } = null!;
    public IProgressSink? ProgressSink { get; init; }
    public IJobMetricsStore? MetricsStore { get; init; }
    public IJobSnapshotStore? SnapshotStore { get; init; }
    public OrganisationEndpoint SourceEndpoint { get; init; } = null!;
    public IReadOnlyList<string>? Projects { get; init; }
}

