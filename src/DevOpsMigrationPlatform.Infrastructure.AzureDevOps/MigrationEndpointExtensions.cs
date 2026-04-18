using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// ADO-internal extension that converts a <see cref="MigrationEndpointOptions"/>
/// to an <see cref="OrganisationEndpoint"/> for use with <see cref="IAzureDevOpsClientFactory"/>
/// and <see cref="IWiqlQueryClientFactory"/>.
/// </summary>
internal static class MigrationEndpointExtensions
{
    /// <summary>
    /// Converts the endpoint options to an <see cref="OrganisationEndpoint"/> suitable for
    /// Azure DevOps REST API client construction.
    /// </summary>
    internal static OrganisationEndpoint ToOrganisationEndpoint(this MigrationEndpointOptions options)
    {
        if (options is AzureDevOpsEndpointOptions ado)
        {
            return new OrganisationEndpoint
            {
                ResolvedUrl = ado.ResolvedUrl,
                Type = ado.Type,
                ApiVersion = ado.ApiVersion,
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = ado.Authentication?.Type ?? AuthenticationType.None,
                    ResolvedAccessToken = ado.Authentication?.ResolvedAccessToken
                }
            };
        }

        // Fallback for any MigrationEndpointOptions that carries URL/auth via virtual methods
        return new OrganisationEndpoint
        {
            ResolvedUrl = options.GetResolvedUrl(),
            Type = options.Type,
            Authentication = new OrganisationEndpointAuthentication()
        };
    }
}
