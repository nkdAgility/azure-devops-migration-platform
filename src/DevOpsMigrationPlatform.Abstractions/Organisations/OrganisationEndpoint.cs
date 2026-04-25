namespace DevOpsMigrationPlatform.Abstractions.Organisations;

/// <summary>
/// Immutable, resolved connection context for an Azure DevOps organisation or TFS collection.
/// Carries only resolved values — no <c>$ENV:VARNAME</c> tokens, no project scope, no <c>Enabled</c> flag.
/// Used by all Abstractions-level service interfaces as the connection context parameter.
/// </summary>
public sealed class OrganisationEndpoint
{
    /// <summary>Effective org/collection URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl { get; init; } = string.Empty;

    /// <summary>Source type identifier (<c>AzureDevOpsServices</c>, <c>TeamFoundationServer</c>).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Resolved authentication context.</summary>
    public OrganisationEndpointAuthentication Authentication { get; init; } = new();

    /// <summary>Pinned REST API version (e.g. <c>7.1</c>). Null means use default.</summary>
    public string? ApiVersion { get; init; }
}
