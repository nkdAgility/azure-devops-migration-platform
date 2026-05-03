// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

/// <summary>
/// Creates <see cref="PhaseTrackingService"/> instances bound to a per-operation <see cref="IStateStore"/>.
/// </summary>
public sealed class PhaseTrackingServiceFactory : IPhaseTrackingServiceFactory
{
    /// <inheritdoc/>
    public IPhaseTrackingService Create(IStateStore stateStore)
        => new PhaseTrackingService(stateStore);
}
