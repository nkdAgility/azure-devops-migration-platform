using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Base class for one entry in the <c>organisations</c> array (Mode 2 config).
/// Contains all standard connection fields so Microsoft.Extensions.Configuration can bind
/// this type directly from a JSON config file (IConfiguration binding path).
/// Connector-specific subclasses (e.g. <c>AzureDevOpsOrganisationEntry</c>) may override
/// <see cref="ToOrganisationEndpoint"/> to add custom logic without changing the binding path.
/// </summary>
public class OrganisationEntry
{
    /// <summary>
    /// Connector type discriminator.
    /// Supported values: <c>AzureDevOpsServices</c>, <c>TeamFoundationServer</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Organisation URL (Azure DevOps Services) or collection URL (TFS).
    /// Supports <c>$ENV:VARNAME</c> resolution.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public virtual string ResolvedUrl => TokenResolver.Resolve(Url) ?? Url;

    /// <summary>Pinned REST API version (e.g. <c>7.1</c>).</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Authentication details for this entry.</summary>
    public EndpointAuthenticationOptions Authentication { get; set; } = new EndpointAuthenticationOptions();

    /// <summary>
    /// Projects to inventory. Empty or absent = all projects in the org/collection.
    /// </summary>
    public List<string> Projects { get; set; } = new List<string>();

    /// <summary>
    /// Set to <c>false</c> to skip this entry without deleting it. Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Creates an immutable <see cref="OrganisationEndpoint"/> from this entry's connection fields.
    /// Override in derived types to provide connector-specific mapping.
    /// </summary>
    public virtual OrganisationEndpoint ToOrganisationEndpoint()
    {
        return new OrganisationEndpoint
        {
            ResolvedUrl = ResolvedUrl,
            Type = Type,
            ApiVersion = ApiVersion,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = Authentication.Type,
                ResolvedAccessToken = Authentication.ResolvedAccessToken
            }
        };
    }

    /// <summary>
    /// Validates connector-specific fields (e.g. URL, authentication).
    /// Override in derived classes to add connector-specific validation.
    /// </summary>
    public virtual void ValidateConnectorFields()
    {
        if (string.IsNullOrWhiteSpace(Url))
            throw new System.InvalidOperationException(
                $"Config error: An organisations entry of type '{Type}' is missing 'url'.");
    }
}
