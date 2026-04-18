using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;

/// <summary>
/// TFS / Azure DevOps Server-specific organisation entry for inventory discovery.
/// Carries all TFS connection fields (URL, auth, API version).
/// </summary>
public sealed class TeamFoundationServerOrganisationEntry : OrganisationEntry
{
    /// <summary>
    /// Collection URL (TFS / Azure DevOps Server).
    /// Supports <c>$ENV:VARNAME</c> resolution.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? Url;

    /// <summary>Pinned REST API version (e.g. <c>7.1</c>).</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Authentication details for this entry.</summary>
    public EndpointAuthenticationOptions Authentication { get; set; } = new EndpointAuthenticationOptions();

    /// <inheritdoc/>
    public override OrganisationEndpoint ToOrganisationEndpoint()
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

    /// <inheritdoc/>
    public override void ValidateConnectorFields()
    {
        if (string.IsNullOrWhiteSpace(Url))
            throw new System.InvalidOperationException(
                $"Config error: An organisations entry of type '{Type}' is missing 'url'.");

        var resolvedUrl = ResolvedUrl;
        if (string.IsNullOrWhiteSpace(resolvedUrl))
            throw new System.InvalidOperationException(
                $"Config error: URL for a 'TeamFoundationServer' entry resolved to an empty string. " +
                "Set 'url' to a literal value or '$ENV:VARNAME'.");

        if (Authentication != null &&
            Authentication.Type == AuthenticationType.Pat)
        {
            var resolved = Authentication.ResolvedAccessToken;
            if (string.IsNullOrWhiteSpace(resolved))
                throw new System.InvalidOperationException(
                    $"Config error: PAT for '{resolvedUrl}' resolved to an empty string. " +
                    "Set 'authentication.accessToken' to a literal value or '$ENV:VARNAME'.");
        }
    }
}
