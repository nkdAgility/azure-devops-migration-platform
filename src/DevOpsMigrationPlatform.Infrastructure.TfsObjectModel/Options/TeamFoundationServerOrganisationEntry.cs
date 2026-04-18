using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;

/// <summary>
/// TFS / Azure DevOps Server-specific organisation entry for inventory discovery.
/// Inherits all standard connection fields from <see cref="OrganisationEntry"/>.
/// </summary>
public sealed class TeamFoundationServerOrganisationEntry : OrganisationEntry
{
    /// <summary>
    /// Creates an immutable <see cref="OrganisationEndpoint"/>.
    /// </summary>
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
        base.ValidateConnectorFields();

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
