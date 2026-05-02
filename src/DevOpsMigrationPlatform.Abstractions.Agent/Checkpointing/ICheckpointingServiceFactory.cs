// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

/// <summary>
/// Creates <see cref="ICheckpointingService"/> instances bound to a specific <see cref="IStateStore"/>.
/// Injected into module and worker classes that receive a per-operation state store at runtime
/// and must not take <see cref="ICheckpointingService"/> as a direct constructor dependency.
/// </summary>
public interface ICheckpointingServiceFactory
{
    /// <summary>
    /// Creates a new <see cref="ICheckpointingService"/> backed by the given <paramref name="stateStore"/>.
    /// </summary>
    ICheckpointingService Create(IStateStore stateStore);
}
