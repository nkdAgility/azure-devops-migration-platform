using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps implementation of <see cref="IInventoryService"/>.
/// Uses <see cref="WorkItemQueryWindowStrategy"/> to keep each WIQL query under 20,000 items,
/// then fetches <c>System.Rev</c> in batches to count revisions.
/// </summary>
public sealed class AzureDevOpsInventoryService : IInventoryService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private const int RevisionBatchSize = 200;

    public AzureDevOpsInventoryService(IWorkItemQueryWindowStrategy windowStrategy)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
    }

    public async IAsyncEnumerable<InventoryProgressEvent> CountWorkItemsAsync(
        string orgOrCollection,
        string project,
        string pat,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgOrCollection), credentials);
        var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

        int totalWorkItems = 0;
        int totalRevisions = 0;

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            orgOrCollection, project, pat, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            totalWorkItems += window.WorkItemIds.Count;

            // Fetch System.Rev in batches
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
                        totalRevisions += c.ToInt32(null);
                }
            }

            yield return new InventoryProgressEvent
            {
                ProjectName = project,
                OrgOrCollection = orgOrCollection,
                WorkItemsCount = totalWorkItems,
                RevisionsCount = totalRevisions,
                IsComplete = false,
                WindowStart = window.WindowStart,
                WindowEnd = window.WindowEnd,
                WindowSize = window.WindowSize,
                Timestamp = DateTime.UtcNow
            };
        }

        yield return new InventoryProgressEvent
        {
            ProjectName = project,
            OrgOrCollection = orgOrCollection,
            WorkItemsCount = totalWorkItems,
            RevisionsCount = totalRevisions,
            IsComplete = true,
            WindowStart = DateTime.MinValue,
            WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.Zero,
            Timestamp = DateTime.UtcNow
        };
    }
}
