using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Jobs;

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
    /// Source endpoint for capability routing. Agents advertise which endpoint
    /// types they support; the control plane uses this to match jobs to agents.
    /// Falls back to <see cref="Organisations"/>[0].Endpoint when not set explicitly.
    /// </summary>
    public MigrationEndpointOptions? Source { get; init; }

    /// <summary>
    /// Organisations / collections to analyse. At least one entry is required.
    /// </summary>
    public List<ScopedOrganisationEndpoint> Organisations { get; init; } = new();

    /// <inheritdoc />
    public override string? GetSourceType() =>
        Source?.Type ?? (Organisations.Count > 0 ? Organisations[0].Endpoint?.Type : null);

}
