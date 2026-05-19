// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

/// <summary>
/// Creates <see cref="IPhaseTrackingService"/> instances bound to a specific <see cref="IPackageAccess"/>.
/// Injected into worker classes that receive a per-operation package at runtime.
/// </summary>
public interface IPhaseTrackingServiceFactory
{
    /// <summary>
    /// Creates a new <see cref="IPhaseTrackingService"/> backed by the given <paramref name="packageAccess"/>.
    /// </summary>
    IPhaseTrackingService Create(IPackageAccess packageAccess);
}
