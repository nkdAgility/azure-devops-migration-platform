using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

public class CatalogService : ICatalogService
{
    private readonly IWorkItemDiscoveryService _workItemDiscovery;

    public CatalogService(IWorkItemDiscoveryService workItemDiscovery)
    {
        _workItemDiscovery = workItemDiscovery ?? throw new ArgumentNullException(nameof(workItemDiscovery));
    }

    public async Task<IReadOnlyList<string>> GetProjectsAsync(
        string orgUrl,
        string pat,
        CancellationToken cancellationToken = default)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgUrl), credentials);
        var projectClient = connection.GetClient<ProjectHttpClient>();
        var projects = await projectClient.GetProjects();
        return projects.Select(p => p.Name).ToList();
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
