using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemRevisionSourceFactory"/>.
/// Constructs an <see cref="AzureDevOpsWorkItemRevisionSource"/> from endpoint options.
/// Accepts either <see cref="AzureDevOpsEndpointOptions"/> (preferred) or
/// <see cref="JobEndpointMigrationOptions"/> (legacy bridge from <c>WorkItemsModule</c>).
/// </summary>
public sealed class AzureDevOpsWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IAzureDevOpsWorkItemRevisionMapper _mapper;
    private readonly AzureDevOpsAttachmentRegistry _registry;

    public AzureDevOpsWorkItemRevisionSourceFactory(
        IAzureDevOpsClientFactory clientFactory,
        IWorkItemQueryWindowStrategy windowStrategy,
        IAzureDevOpsWorkItemRevisionMapper mapper,
        AzureDevOpsAttachmentRegistry registry)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        OrganisationEndpoint organisationEndpoint;
        string project;
        string? wiqlQuery = null;

        if (endpoint is AzureDevOpsEndpointOptions adoEndpoint)
        {
            organisationEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = adoEndpoint.ResolvedUrl,
                Type = adoEndpoint.Type,
                ApiVersion = adoEndpoint.ApiVersion,
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = adoEndpoint.Authentication?.Type ?? AuthenticationType.None,
                    ResolvedAccessToken = adoEndpoint.Authentication?.ResolvedAccessToken
                }
            };
            project = adoEndpoint.Project;
        }
        else if (endpoint is JobEndpointMigrationOptions jobEndpointOptions)
        {
            var je = jobEndpointOptions.JobEndpoint;
            organisationEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = je.ResolvedUrl,
                Type = je.Type,
                ApiVersion = je.ApiVersion,
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = je.Authentication?.Type ?? AuthenticationType.None,
                    ResolvedAccessToken = je.Authentication?.ResolvedAccessToken
                }
            };
            project = je.Project;
        }
        else
        {
            throw new ArgumentException(
                $"Expected AzureDevOpsEndpointOptions or JobEndpointMigrationOptions but got {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(organisationEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemRevisionSource(
            witClient, _windowStrategy, _mapper, _registry, organisationEndpoint, project, wiqlQuery);
    }
}
