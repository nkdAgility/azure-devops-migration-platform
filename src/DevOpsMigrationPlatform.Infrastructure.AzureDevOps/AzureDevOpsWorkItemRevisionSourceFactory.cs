using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Attachments;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemRevisionSourceFactory"/>.
/// Constructs an <see cref="AzureDevOpsWorkItemRevisionSource"/> from endpoint options resolved via DI.
/// </summary>
internal sealed class AzureDevOpsWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IAzureDevOpsWorkItemRevisionMapper _mapper;
    private readonly AzureDevOpsAttachmentRegistry _registry;
    private readonly ISourceEndpointInfo _endpointInfo;

    public AzureDevOpsWorkItemRevisionSourceFactory(
        IAzureDevOpsClientFactory clientFactory,
        IWorkItemQueryWindowStrategy windowStrategy,
        IAzureDevOpsWorkItemRevisionMapper mapper,
        AzureDevOpsAttachmentRegistry registry,
        ISourceEndpointInfo endpointInfo)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemRevisionSource> CreateAsync(CancellationToken ct)
    {
        var organisationEndpoint = new OrganisationEndpoint
        {
            ResolvedUrl = _endpointInfo.Url,
            Type = _endpointInfo.ConnectorType
        };
        var project = _endpointInfo.Project;

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(organisationEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemRevisionSource(
            witClient, _windowStrategy, _mapper, _registry, organisationEndpoint, project, wiqlQuery: null);
    }
}
