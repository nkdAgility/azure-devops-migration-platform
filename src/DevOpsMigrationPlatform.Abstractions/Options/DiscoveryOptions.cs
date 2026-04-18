using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the <c>devopsmigration discovery</c> command group.
/// Bound from the <c>MigrationPlatform</c> configuration section.
/// Contains the multi-org roster to discover.
/// Environment topology (Standalone / Hosted) is controlled by the separate
/// <c>MigrationPlatform:Environment</c> section, which is bound independently by the host builder.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>Retry, throttle, and checkpoint policies.</summary>
    public MigrationPoliciesOptions Policies { get; set; } = new();

    /// <summary>Organisations / collections to inventory.</summary>
    public List<OrganisationEntry> Organisations { get; set; } = new();

    /// <summary>
    /// Output location for discovery results.
    /// Required. The CLI normalises <see cref="MigrationArtefactsOptions.Path"/> to a
    /// <c>file:///</c> URI (standalone) or passes a blob HTTPS URL (hosted) before building
    /// the <see cref="DevOpsMigrationPlatform.Abstractions.DiscoveryJob"/>.
    /// </summary>
    public MigrationArtefactsOptions Artefacts { get; set; } = new();

    /// <summary>
    /// Validates the options, throwing <see cref="InvalidOperationException"/> on any violation.
    /// Called at command startup before any API calls.
    /// </summary>
    public void Validate()
    {
        Policies.Validate();

        if (string.IsNullOrWhiteSpace(Artefacts.WorkingDirectory))
            throw new InvalidOperationException(
                "Config error: 'Artefacts.WorkingDirectory' is required for discovery commands. " +
                "Add an 'Artefacts' section with a 'WorkingDirectory' to your config file.");

        if (Organisations.Count == 0)
            throw new InvalidOperationException("Config error: 'organisations' array is empty.");

        foreach (var entry in Organisations)
        {
            if (string.IsNullOrWhiteSpace(entry.Type))
                throw new InvalidOperationException(
                    "Config error: An organisations entry is missing 'type'.");

            entry.ValidateConnectorFields();
        }
    }
}
