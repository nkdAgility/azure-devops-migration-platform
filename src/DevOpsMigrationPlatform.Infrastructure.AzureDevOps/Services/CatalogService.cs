using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

public class CatalogService : ICatalogService
{
    private readonly IWorkItemDiscoveryService _workItemDiscovery;
    private readonly IProjectDiscoveryService _projectDiscovery;

    public CatalogService(
        IWorkItemDiscoveryService workItemDiscovery,
        IProjectDiscoveryService projectDiscovery)
    {
        _workItemDiscovery = workItemDiscovery ?? throw new ArgumentNullException(nameof(workItemDiscovery));
        _projectDiscovery = projectDiscovery ?? throw new ArgumentNullException(nameof(projectDiscovery));
    }

    public async Task<IReadOnlyList<string>> GetProjectsAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.DiscoverProjectsAsync(endpoint, cancellationToken);
        return projects;
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> CountAllWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
            endpoint, project, cancellationToken))
        {
            yield return summary;
        }
    }
}
