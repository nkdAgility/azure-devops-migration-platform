using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

/// <summary>
/// Simulated organisation entry for inventory discovery.
/// Requires no credentials. All project names and work item counts are generated
/// deterministically from <see cref="Generator"/>.
/// </summary>
public sealed class SimulatedOrganisationEntry : OrganisationEntry
{
    /// <summary>Generator configuration for synthetic data.</summary>
    public SimulatedGeneratorConfig Generator { get; set; } = new();

    /// <summary>
    /// Returns an <see cref="OrganisationEndpoint"/> with <c>Type = "Simulated"</c>
    /// and a placeholder URL.
    /// </summary>
    public override OrganisationEndpoint ToOrganisationEndpoint()
    {
        return new OrganisationEndpoint
        {
            ResolvedUrl = "simulated://localhost",
            Type = "Simulated",
            ApiVersion = null,
            Authentication = new OrganisationEndpointAuthentication()
        };
    }

    /// <inheritdoc/>
    public override void ValidateConnectorFields()
    {
        // No required fields for simulated org entries.
    }
}
