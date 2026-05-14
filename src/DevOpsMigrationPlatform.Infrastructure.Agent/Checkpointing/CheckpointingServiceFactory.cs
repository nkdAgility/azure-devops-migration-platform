// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

/// <summary>
/// Creates <see cref="CheckpointingService"/> instances bound to a per-operation <see cref="IStateStore"/>.
/// </summary>
public sealed class CheckpointingServiceFactory : ICheckpointingServiceFactory
{
    private readonly ICurrentJobEndpointAccessor _currentJobEndpointAccessor;
    private readonly ICurrentPackageConfigAccessor _currentPackageConfigAccessor;
    private readonly IPackageAccess _package;

    public CheckpointingServiceFactory(
        ICurrentJobEndpointAccessor currentJobEndpointAccessor,
        ICurrentPackageConfigAccessor currentPackageConfigAccessor,
        IPackageAccess package)
    {
        _currentJobEndpointAccessor = currentJobEndpointAccessor;
        _currentPackageConfigAccessor = currentPackageConfigAccessor;
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    /// <inheritdoc/>
    public ICheckpointingService Create(IStateStore stateStore)
        => new CheckpointingService(
            _currentJobEndpointAccessor,
            _currentPackageConfigAccessor,
            package: _package);
}
