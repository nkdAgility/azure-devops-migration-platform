using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Source or target endpoint connection options.  Used for both <c>MigrationOptions.Source</c>
/// and <c>MigrationOptions.Target</c>.
/// </summary>
public class MigrationEndpointOptions
{
    /// <summary>
    /// Endpoint kind.  Supported values: <c>AzureDevOpsServices</c>, <c>TeamFoundationServer</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Organisation URL (Azure DevOps Services) or collection URL (TFS/Azure DevOps Server).
    /// May contain a <c>$ENV:VARNAME</c> reference — use <see cref="ResolvedUrl"/> for API calls.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? string.Empty;

    /// <summary>Team project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// REST API version to request (e.g. <c>7.1</c>).
    /// Leave empty to use the server-negotiated default.
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Authentication credentials for this endpoint.
    /// Optional for backwards compatibility; required for inventory and new export/import flows.
    /// </summary>
    public EndpointAuthenticationOptions? Authentication { get; set; }
}
