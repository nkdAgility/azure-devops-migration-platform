// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Context passed to <see cref="IModule.PrepareAsync(PrepareContext, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed record PrepareContext
{
    public Job Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public IStateStore StateStore { get; init; } = null!;
    public IProgressSink? ProgressSink { get; init; }
    public ITargetEndpointInfo TargetEndpoint { get; init; } = null!;
}

