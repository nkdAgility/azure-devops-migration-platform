using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Source or target system connection in a MigrationJob.</summary>
public class MigrationJobEndpoint
{
    /// <summary>AzureDevOpsServices or TeamFoundationServer.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Organisation URL (AZDO) or collection URL (TFS).</summary>
    public string OrgOrCollection { get; init; } = string.Empty;

    /// <summary>Team project name.</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>API version string (AZDO) or leave empty for TFS.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Authentication credentials for this endpoint.
    /// Carried from the config into the job contract so that Migration Agents
    /// can authenticate to both REST (ADO) and Object Model (TFS) sources
    /// without requiring a separate credential lookup.
    /// </summary>
    public EndpointAuthenticationOptions? Authentication { get; init; }
}
