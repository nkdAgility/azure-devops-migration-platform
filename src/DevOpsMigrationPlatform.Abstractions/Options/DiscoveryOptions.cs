using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the <c>devopsmigration discovery</c> command group.
/// Contains the multi-org roster to discover.
/// </summary>
public sealed class DiscoveryOptions
{
    public const string SectionName = "MigrationTools";

    public string ConfigVersion { get; set; } = "1.0";

    /// <summary>Organisations / collections to inventory.</summary>
    public List<OrganisationEntry> Organisations { get; set; } = new();

    /// <summary>
    /// Validates the options, throwing <see cref="InvalidOperationException"/> on any violation.
    /// Called at command startup before any API calls.
    /// </summary>
    public void Validate()
    {
        if (Organisations.Count == 0)
            throw new InvalidOperationException("Config error: 'organisations' array is empty.");

        foreach (var entry in Organisations)
        {
            if (string.IsNullOrWhiteSpace(entry.Type))
                throw new InvalidOperationException(
                    "Config error: An organisations entry is missing 'type'.");
            if (string.IsNullOrWhiteSpace(entry.OrgOrCollection))
                throw new InvalidOperationException(
                    $"Config error: An organisations entry of type '{entry.Type}' is missing 'orgOrCollection'.");

            if (entry.Authentication != null &&
                string.Equals(entry.Authentication.Type, "Pat", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = TokenResolver.Resolve(entry.Authentication.AccessToken);
                if (string.IsNullOrWhiteSpace(resolved))
                    throw new InvalidOperationException(
                        $"Config error: PAT for '{entry.OrgOrCollection}' resolved to an empty string. " +
                        "Set 'authentication.accessToken' to a literal value or '$ENV:VARNAME'.");
            }
        }
    }
}
