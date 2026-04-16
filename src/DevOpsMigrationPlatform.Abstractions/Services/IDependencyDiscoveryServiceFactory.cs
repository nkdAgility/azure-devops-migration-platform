using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Factory that creates a configured <see cref="IDependencyDiscoveryService"/> from a list of
/// <see cref="DiscoveryJobOrganisation"/> entries supplied at runtime.
/// Required because the agent receives organisations via the <see cref="DiscoveryJob"/>
/// contract rather than from a config file bound at host startup.
/// </summary>
public interface IDependencyDiscoveryServiceFactory
{
    /// <summary>
    /// Creates an <see cref="IDependencyDiscoveryService"/> scoped to the provided organisations.
    /// </summary>
    IDependencyDiscoveryService Create(
        IReadOnlyList<DevOpsMigrationPlatform.Abstractions.DiscoveryJobOrganisation> organisations,
        DevOpsMigrationPlatform.Abstractions.JobPolicies policies);
}
