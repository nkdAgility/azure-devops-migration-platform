// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

/// <summary>
/// Creates <see cref="PhaseTrackingService"/> instances bound to a per-operation <see cref="IStateStore"/>.
/// </summary>
public sealed class PhaseTrackingServiceFactory : IPhaseTrackingServiceFactory
{
    private readonly IPackageAccess _package;

    public PhaseTrackingServiceFactory(IPackageAccess package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    /// <inheritdoc/>
    public IPhaseTrackingService Create(IPackageAccess packageAccess)
        => new PhaseTrackingService(packageAccess ?? throw new ArgumentNullException(nameof(packageAccess)));
}
