using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Discovers team projects via the Azure DevOps REST API.
/// </summary>
public sealed class AzureDevOpsProjectDiscoveryService : IProjectDiscoveryService
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsProjectDiscoveryService(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<List<string>> DiscoverProjectsAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var projectClient = await _clientFactory.CreateProjectClientAsync(endpoint, cancellationToken);
        var projects = await projectClient.GetProjects();
        return projects.Select(p => p.Name).ToList();
    }
}
