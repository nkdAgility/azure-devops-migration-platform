using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// The internal serialisable unit of execution for discovery operations.
/// Handed from the CLI to Control Plane to Migration Agent.
/// Inherits common fields from <see cref="Job"/>.
/// No target, no modules list - the agent resolves the correct IDiscoveryModule from DiscoveryType.
/// </summary>
public class DiscoveryJob : Job
{
    /// <summary>Which discovery operations to run.</summary>
    public DiscoveryJobType DiscoveryType { get; init; } = DiscoveryJobType.Inventory;

    /// <summary>
    /// Organisations / collections to analyse. At least one entry is required.
    /// </summary>
    public List<ScopedOrganisationEndpoint> Organisations { get; init; } = new();


}
