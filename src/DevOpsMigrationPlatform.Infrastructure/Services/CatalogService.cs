using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="ICatalogService"/> that delegates to
/// <see cref="IProjectDiscoveryService"/> and <see cref="IWorkItemDiscoveryService"/>.
/// Connector-agnostic: works with any discovery service implementation.
/// </summary>
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
        MigrationEndpointOptions endpoint,
        CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.DiscoverProjectsAsync(endpoint, cancellationToken);
        return projects;
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> CountAllWorkItemsAsync(
        MigrationEndpointOptions endpoint,
        string project,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var orgEndpoint = endpoint.ToOrganisationEndpoint();
        await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
            orgEndpoint, project, cancellationToken))
        {
            yield return summary;
        }
    }
}
