using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the <c>devopsmigration discovery</c> command group.
/// Contains the multi-org roster to discover.
/// </summary>
public sealed class DiscoveryOptions
{

    public string ConfigVersion { get; set; } = "1.0";

    /// <summary>Organisations / collections to inventory.</summary>
    public List<OrganisationEntry> Organisations { get; set; } = new();

    /// <summary>
    /// Maximum concurrent batch requests to source during dependency analysis.
    /// Default is 4. Binds from JSON config key 'maxConcurrency' (snake_case per convention).
    /// Prevents rate-limit triggers during parallel link fetching.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// How often (in seconds) in-progress output is flushed to disk during dependency analysis.
    /// Protects against data loss on long runs. Default is 300 (5 minutes).
    /// Set to a lower value (e.g. 60) for very large orgs where a crash would be costly.
    /// </summary>
    public int CheckpointIntervalSeconds { get; set; } = 300;

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
