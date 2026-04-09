using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Source or target system connection in a MigrationJob.</summary>
public class MigrationJobEndpoint
{
    /// <summary>AzureDevOpsServices or TeamFoundationServer.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Organisation URL (AZDO) or collection URL (TFS).
    /// May contain a <c>$ENV:VARNAME</c> reference — use <see cref="ResolvedUrl"/> for API calls.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? string.Empty;

    /// <summary>Team project name.</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>API version string (AZDO) or leave empty for TFS.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Authentication credentials for this endpoint.
    /// Carried from the config into the job contract so that Migration Agents
    /// can authenticate to both REST (Azure DevOps) and Object Model (TFS) sources
    /// without requiring a separate credential lookup.
    /// </summary>
    public EndpointAuthenticationOptions? Authentication { get; init; }
}
