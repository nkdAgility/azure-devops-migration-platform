// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

/// <summary>
/// Creates <see cref="CheckpointingService"/> instances bound to a per-operation <see cref="IStateStore"/>.
/// </summary>
public sealed class CheckpointingServiceFactory : ICheckpointingServiceFactory
{
    private readonly ICurrentJobEndpointAccessor? _currentJobEndpointAccessor;
    private readonly ICurrentPackageConfigAccessor? _currentPackageConfigAccessor;

    public CheckpointingServiceFactory(
        ICurrentJobEndpointAccessor? currentJobEndpointAccessor = null,
        ICurrentPackageConfigAccessor? currentPackageConfigAccessor = null)
    {
        _currentJobEndpointAccessor = currentJobEndpointAccessor;
        _currentPackageConfigAccessor = currentPackageConfigAccessor;
    }

    /// <inheritdoc/>
    public ICheckpointingService Create(IStateStore stateStore)
        => new CheckpointingService(stateStore, _currentJobEndpointAccessor, _currentPackageConfigAccessor);
}
