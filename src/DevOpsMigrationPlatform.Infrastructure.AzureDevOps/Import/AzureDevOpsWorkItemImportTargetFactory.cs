using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Infrastructure.Modules;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Creates an <see cref="AzureDevOpsWorkItemImportTarget"/> from <see cref="AzureDevOpsEndpointOptions"/>
/// or <see cref="JobEndpointMigrationOptions"/>.
/// Throws <see cref="ArgumentException"/> for any other endpoint type.
/// </summary>
public sealed class AzureDevOpsWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsWorkItemImportTargetFactory(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemImportTarget> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        OrganisationEndpoint orgEndpoint;
        string project;
        string resolvedUrl;

        if (endpoint is AzureDevOpsEndpointOptions adoEndpoint)
        {
            orgEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = adoEndpoint.ResolvedUrl,
                Type = adoEndpoint.Type,
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = adoEndpoint.Authentication?.Type ?? AuthenticationType.Pat,
                    ResolvedAccessToken = adoEndpoint.Authentication?.ResolvedAccessToken
                }
            };
            project = adoEndpoint.Project;
            resolvedUrl = adoEndpoint.ResolvedUrl;
        }
        else if (endpoint is JobEndpointMigrationOptions jobEndpointOptions)
        {
            var je = jobEndpointOptions.JobEndpoint;
            orgEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = je.ResolvedUrl,
                Type = je.Type,
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = je.Authentication?.Type ?? AuthenticationType.Pat,
                    ResolvedAccessToken = je.Authentication?.ResolvedAccessToken
                }
            };
            project = je.Project;
            resolvedUrl = je.ResolvedUrl;
        }
        else
        {
            throw new ArgumentException(
                $"Expected AzureDevOpsEndpointOptions or JobEndpointMigrationOptions but got {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(orgEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemImportTarget(witClient, project, resolvedUrl);
    }
}
