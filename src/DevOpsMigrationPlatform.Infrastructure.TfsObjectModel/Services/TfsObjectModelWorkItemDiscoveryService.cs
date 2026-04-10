using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemDiscoveryService"/>.
/// Uses the COM-based WorkItemStore to enumerate work items and count revisions.
/// Runs in the .NET Framework 4.8.1 subprocess.
/// </summary>
public sealed class TfsObjectModelWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        string collectionUrl,
        string project,
        string pat,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = new System.Net.NetworkCredential(string.Empty, pat);
        var tfsUri = new Uri(collectionUrl);
        var tpc = new TfsTeamProjectCollection(tfsUri, credentials);
        tpc.EnsureAuthenticated();

        var store = tpc.GetService<WorkItemStore>();

        var wiql = $"SELECT [System.Id] FROM WorkItems " +
                   $"WHERE [System.TeamProject] = '{EscapeWiql(project)}' " +
                   $"ORDER BY [System.Id]";

        var query = new Query(store, wiql);
        var results = query.RunCountQuery();

        var summary = new ProjectDiscoverySummary { ProjectName = project };

        // Page through work items to count IDs and revisions
        var idQuery = new Query(store, wiql);
        var workItemIds = idQuery.RunQuery()
            .Cast<WorkItem>()
            .Select(wi => wi.Id)
            .ToList();

        const int batchSize = 200;
        var processed = 0;

        foreach (var batch in Chunk(workItemIds, batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var id in batch)
            {
                var wi = store.GetWorkItem(id);
                summary.WorkItemsCount++;
                summary.RevisionsCount += wi.Revisions.Count;
            }

            processed += batch.Count;
            summary.LastUpdatedUtc = DateTime.UtcNow;
            yield return summary;
        }

        summary.IsWorkItemComplete = true;
        summary.LastUpdatedUtc = DateTime.UtcNow;
        yield return summary;
    }

    /// <summary>
    /// TFS Object Model does not support custom WIQL-scoped pre-flight counting.
    /// Delegates to <see cref="DiscoverWorkItemsAsync"/> using the full project scope.
    /// The <paramref name="baseQuery"/> parameter is intentionally ignored.
    /// </summary>
    public IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        string url,
        string project,
        string pat,
        string? baseQuery = null,
        CancellationToken cancellationToken = default)
        => DiscoverWorkItemsAsync(url, project, pat, cancellationToken);

    private static string EscapeWiql(string value)
        => value.Replace("'", "''");

    private static List<List<T>> Chunk<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        }
        return chunks;
    }
}
