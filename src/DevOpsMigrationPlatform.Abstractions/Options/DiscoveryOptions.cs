using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the <c>devopsmigration discovery</c> command group.
/// Bound from the <c>MigrationPlatform</c> configuration section.
/// Contains the multi-org roster to discover.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>Retry, throttle, and checkpoint policies.</summary>
    public MigrationPoliciesOptions Policies { get; set; } = new();

    /// <summary>Organisations / collections to inventory.</summary>
    public List<OrganisationEntry> Organisations { get; set; } = new();

    /// <summary>
    /// Validates the options, throwing <see cref="InvalidOperationException"/> on any violation.
    /// Called at command startup before any API calls.
    /// </summary>
    public void Validate()
    {
        Policies.Validate();

        if (Organisations.Count == 0)
            throw new InvalidOperationException("Config error: 'organisations' array is empty.");

        foreach (var entry in Organisations)
        {
            if (string.IsNullOrWhiteSpace(entry.Type))
                throw new InvalidOperationException(
                    "Config error: An organisations entry is missing 'type'.");
            if (string.IsNullOrWhiteSpace(entry.Url))
                throw new InvalidOperationException(
                    $"Config error: An organisations entry of type '{entry.Type}' is missing 'url'.");

            var resolvedUrl = entry.ResolvedUrl;
            if (string.IsNullOrWhiteSpace(resolvedUrl))
                throw new InvalidOperationException(
                    $"Config error: URL for a '{entry.Type}' entry resolved to an empty string. " +
                    "Set 'url' to a literal value or '$ENV:VARNAME'.");

            if (entry.Authentication != null &&
                entry.Authentication.Type == AuthenticationType.Pat)
            {
                var resolved = entry.Authentication.ResolvedAccessToken;
                if (string.IsNullOrWhiteSpace(resolved))
                    throw new InvalidOperationException(
                        $"Config error: PAT for '{resolvedUrl}' resolved to an empty string. " +
                        "Set 'authentication.accessToken' to a literal value or '$ENV:VARNAME'.");
            }
        }
    }
}
