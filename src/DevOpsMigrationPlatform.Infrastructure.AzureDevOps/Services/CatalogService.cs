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
        string orgUrl,
        string pat,
        CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.GetProjectsAsync(orgUrl, pat, cancellationToken);
        return projects;
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> CountAllWorkItemsAsync(
        string orgUrl,
        string project,
        string pat,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
            orgUrl, project, pat, cancellationToken))
        {
            yield return summary;
        }
    }
}
