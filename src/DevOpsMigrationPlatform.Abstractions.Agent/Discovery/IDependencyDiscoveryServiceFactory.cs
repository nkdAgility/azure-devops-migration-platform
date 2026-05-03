// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Factory that creates a configured <see cref="IDependencyDiscoveryService"/> from a list of
/// <see cref="ScopedOrganisationEndpoint"/> entries supplied at runtime.
/// Required because the agent receives organisations via the migration config
/// contract rather than from a config file bound at host startup.
/// </summary>
public interface IDependencyDiscoveryServiceFactory
{
    /// <summary>
    /// Creates an <see cref="IDependencyDiscoveryService"/> scoped to the provided organisations.
    /// </summary>
    IDependencyDiscoveryService Create(
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        JobPolicies policies);
}
