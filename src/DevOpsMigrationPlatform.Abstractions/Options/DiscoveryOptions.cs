using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Options for discovery/inventory commands. Wraps organisation connection details
/// without requiring migration-specific fields like Mode, Target, or Artefacts.
/// Exactly one of <see cref="Source"/> or <see cref="Organisations"/> must be set.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>Mode 1 — reuse an existing migration config source block.</summary>
    public MigrationEndpointOptions? Source { get; set; }

    /// <summary>Mode 2 — multi-org tooling roster.</summary>
    public List<OrganisationEntry>? Organisations { get; set; }

    /// <summary>
    /// Validates the options, throwing <see cref="InvalidOperationException"/> on any violation.
    /// </summary>
    /// <param name="allProjectsFlag">Value of the <c>--all-projects</c> CLI flag (Mode 1 only).</param>
    public void Validate(bool allProjectsFlag)
    {
        bool hasSource = Source != null;
        bool hasOrganisations = Organisations != null;

        if (hasSource && hasOrganisations)
            throw new InvalidOperationException(
                "Config error: 'source' and 'organisations' are mutually exclusive.");

        if (!hasSource && !hasOrganisations)
            throw new InvalidOperationException(
                "Config error: Config must contain either a 'source' block or an 'organisations' array.");

        if (hasSource)
        {
            var source = Source!;
            if (string.IsNullOrWhiteSpace(source.Project) && !allProjectsFlag)
                throw new InvalidOperationException(
                    "Config error: 'source.project' is not set. Specify a project or pass --all-projects.");

            if (source.Authentication != null &&
                string.Equals(source.Authentication.Type, "Pat", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(source.Authentication.AccessToken))
                throw new InvalidOperationException(
                    $"Config error: PAT for '{source.OrgOrCollection}' resolved to an empty string.");
        }

        if (hasOrganisations)
        {
            if (Organisations!.Count == 0)
                throw new InvalidOperationException("Config error: 'organisations' array is empty.");

            foreach (var entry in Organisations)
            {
                if (string.IsNullOrWhiteSpace(entry.Type))
                    throw new InvalidOperationException(
                        "Config error: An organisations entry is missing 'type'.");
                if (string.IsNullOrWhiteSpace(entry.OrgOrCollection))
                    throw new InvalidOperationException(
                        $"Config error: An organisations entry of type '{entry.Type}' is missing 'orgOrCollection'.");
            }
        }
    }
}
