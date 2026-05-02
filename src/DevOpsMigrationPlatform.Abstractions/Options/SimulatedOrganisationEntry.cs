// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Abstractions.Options;

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
    /// Returns a <see cref="SimulatedEndpointOptions"/> with <c>Type = "Simulated"</c>
    /// and the generator configuration.
    /// </summary>
    public override MigrationEndpointOptions ToEndpointOptions()
    {
        return new SimulatedEndpointOptions
        {
            Type = "Simulated",
            Generator = Generator
        };
    }

    /// <inheritdoc/>
    public override void ValidateConnectorFields()
    {
        // No required fields for simulated org entries.
    }
}
