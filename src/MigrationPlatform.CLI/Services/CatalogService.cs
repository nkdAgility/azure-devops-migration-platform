using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using MigrationPlatform.CLI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace MigrationPlatform.CLI.Services
{
    public class CatalogService : ICatalogService
    {

        public async Task<IReadOnlyList<TeamProjectReference>> GetProjectsAsync(string orgUrl,string pat)
        {
            var credentials = new VssBasicCredential(string.Empty, pat);
            var connection = new VssConnection(new Uri(orgUrl), credentials);

            var projectClient = connection.GetClient<ProjectHttpClient>();

            var projects = await projectClient.GetProjects();
            return projects.ToList();
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

            ProjectDiscoverySummary workItemStats = new ProjectDiscoverySummary();
            int lastId = 0;
            int batchCount;
            const int maxPerBatch = 20000;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                string queryText = $"SELECT [System.Id], [System.Rev] FROM WorkItems WHERE [System.TeamProject] = '{project}' AND [System.Id] > {lastId} ORDER BY [System.Id]";
                var query = new Wiql { Query = queryText };

                var workItemIds = await witClient.QueryByWiqlAsync(query, project, cancellationToken: cancellationToken);
                var ids = workItemIds.WorkItems.Select(wi => wi.Id).ToList();
        
                batchCount = ids.Count;
                workItemStats.WorkItemsCount += batchCount;

                if (batchCount > 0)
                {
                    lastId = ids.Max();
                    yield return workItemStats;
                    // Get work item revisions
                    const int pageSize = 200;
                    foreach (var chunk in ids.Chunk(pageSize))
                    {
                        var workItems = await witClient.GetWorkItemsAsync(
                            chunk.ToList(),
                            fields: new[] { "System.Rev" },
                            cancellationToken: cancellationToken);

                        foreach (var item in workItems)
                        {
                            if (item.Fields.TryGetValue("System.Rev", out var revObj) && revObj is IConvertible convertible)
                            {
                                workItemStats.RevisionsCount += convertible.ToInt32(null);
                            }
                        }
                    }
                    yield return workItemStats;
                }

                workItemStats.IsWorkItemComplete = true;
                yield return workItemStats;


            } while (batchCount == maxPerBatch);
        }

        
    }
}
