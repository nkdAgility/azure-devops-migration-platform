using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

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
        if (endpoint is not AzureDevOpsEndpointOptions adoEndpoint)
        {
            throw new ArgumentException(
                $"Expected AzureDevOpsEndpointOptions but got {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        var organisationEndpoint = endpoint.ToOrganisationEndpoint();
        var project = adoEndpoint.Project;

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(organisationEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemRevisionSource(
            witClient, _windowStrategy, _mapper, _registry, organisationEndpoint, project, wiqlQuery: null);
    }
}
