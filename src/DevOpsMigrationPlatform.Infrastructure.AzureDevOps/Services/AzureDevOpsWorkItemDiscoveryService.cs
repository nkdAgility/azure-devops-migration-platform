using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Uses the shared <see cref="IWorkItemQueryWindowStrategy"/> for date-window WIQL
/// queries and fetches <c>System.Rev</c> to tally revision counts.
/// </summary>
public sealed class AzureDevOpsWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private const int RevisionBatchSize = 200;

    public AzureDevOpsWorkItemDiscoveryService(IWorkItemQueryWindowStrategy windowStrategy)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
    }

    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        string orgOrCollection,
        string project,
        string pat,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgOrCollection), credentials);
        var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

        var summary = new ProjectDiscoverySummary { ProjectName = project };

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            orgOrCollection, project, pat, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            summary.WorkItemsCount += window.WorkItemIds.Count;

            foreach (var batch in window.WorkItemIds.Chunk(RevisionBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workItems = await witClient.GetWorkItemsAsync(
                    batch.ToList(),
                    fields: new[] { "System.Rev" },
                    cancellationToken: cancellationToken);

                foreach (var wi in workItems)
                {
                    if (wi.Fields.TryGetValue("System.Rev", out var revObj) && revObj is IConvertible c)
                        summary.RevisionsCount += c.ToInt32(null);
                }
            }

            summary.LastUpdatedUtc = DateTime.UtcNow;
            yield return summary;
        }

        summary.IsWorkItemComplete = true;
        summary.LastUpdatedUtc = DateTime.UtcNow;
        yield return summary;
    }
}
