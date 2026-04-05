using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

public class CatalogService : ICatalogService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private const int PageSize = 200;
    private const int MaxPerBatch = 20_000;

    public CatalogService(IWorkItemQueryWindowStrategy windowStrategy)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
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
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgUrl), credentials);
        var witClient = connection.GetClient<WorkItemTrackingHttpClient>();

        var workItemStats = new ProjectDiscoverySummary { ProjectName = project };

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            orgUrl, project, pat, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            workItemStats.WorkItemsCount += window.WorkItemIds.Count;

            foreach (var chunk in window.WorkItemIds.Chunk(PageSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workItems = await witClient.GetWorkItemsAsync(
                    chunk.ToList(),
                    fields: new[] { "System.Rev" },
                    cancellationToken: cancellationToken);

                foreach (var item in workItems)
                {
                    if (item.Fields.TryGetValue("System.Rev", out var revObj) &&
                        revObj is IConvertible convertible)
                    {
                        workItemStats.RevisionsCount += convertible.ToInt32(null);
                    }
                }
            }

            workItemStats.LastUpdatedUtc = DateTime.UtcNow;
            yield return workItemStats;
        }

        workItemStats.IsWorkItemComplete = true;
        yield return workItemStats;
    }
}
