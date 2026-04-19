using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Factory that creates a configured <see cref="IInventoryService"/> from a list of
/// <see cref="ScopedOrganisationEndpoint"/> entries supplied at runtime.
/// Required because the agent receives organisations via the <see cref="DiscoveryJob"/>
/// contract rather than from a config file bound at host startup.
/// </summary>
public interface IInventoryServiceFactory
{
    /// <summary>
    /// Creates an <see cref="IInventoryService"/> scoped to the provided organisations.
    /// </summary>
    IInventoryService Create(
        IReadOnlyList<DevOpsMigrationPlatform.Abstractions.ScopedOrganisationEndpoint> organisations,
        DevOpsMigrationPlatform.Abstractions.JobPolicies policies);
}
