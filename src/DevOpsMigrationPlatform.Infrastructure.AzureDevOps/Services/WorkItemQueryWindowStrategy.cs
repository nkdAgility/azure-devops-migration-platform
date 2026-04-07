using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Shared date-window WIQL strategy that keeps each query under the 20,000-item limit.
/// Used by both <see cref="InventoryService"/> (counting) and
/// <see cref="CatalogService"/> (export paging).
/// </summary>
public sealed class WorkItemQueryWindowStrategy : IWorkItemQueryWindowStrategy
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public WorkItemQueryWindowStrategy(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }
    /// <summary>
    /// Enumerates successive date windows for <paramref name="project"/>, each containing
    /// the work item IDs created in that window. Windows are walked backward from
    /// <see cref="DateTime.UtcNow"/> until a window yields zero results.
    ///
    /// If a window returns ≥ <see cref="WorkItemQueryWindowOptions.LimitThreshold"/> items,
    /// the window is halved and retried (the partial result is NOT yielded).
    /// On WIQL failure, the window is halved and retried up to 3 times.
    /// After a successful narrow window (&lt; 30 days) the window grows by 1 day.
    /// </summary>
    public async IAsyncEnumerable<WorkItemQueryWindow> EnumerateWindowsAsync(
        string orgOrCollection,
        string project,
        string pat,
        WorkItemQueryWindowOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new WorkItemQueryWindowOptions();

        var witClient = await _clientFactory.CreateWorkItemClientAsync(orgOrCollection, pat, cancellationToken);

        var windowSize = TimeSpan.FromDays(options.InitialWindowDays);
        var windowEnd = DateTime.UtcNow;
        const int maxRetries = 3;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windowStart = windowEnd - windowSize;

            var wiql = new Wiql
            {
                Query = $"SELECT [System.Id] FROM WorkItems " +
                        $"WHERE [System.TeamProject] = '{EscapeWiql(project)}' " +
                        $"AND [System.CreatedDate] >= '{windowStart:yyyy-MM-dd}' " +
                        $"AND [System.CreatedDate] < '{windowEnd:yyyy-MM-dd}' " +
                        $"ORDER BY [System.Id]"
            };

            List<int> ids;
            int retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await witClient.QueryByWiqlAsync(wiql, project, cancellationToken: cancellationToken);
                    ids = result.WorkItems.Select(r => r.Id).ToList();
                    break;
                }
                catch (Exception ex) when (retries < maxRetries && ex is not OperationCanceledException)
                {
                    retries++;
                    windowSize = TimeSpan.FromTicks(windowSize.Ticks / 2);
                    if (windowSize < TimeSpan.FromDays(options.MinWindowDays))
                        windowSize = TimeSpan.FromDays(options.MinWindowDays);
                    windowStart = windowEnd - windowSize;
                    wiql.Query = wiql.Query
                        .Replace($">= '{(windowEnd - TimeSpan.FromTicks(windowSize.Ticks * 2)):yyyy-MM-dd}'",
                                 $">= '{windowStart:yyyy-MM-dd}'");
                    continue;
                }
            }

            if (ids.Count >= options.LimitThreshold)
            {
                // Window too large — halve and retry without yielding
                windowSize = TimeSpan.FromTicks(windowSize.Ticks / 2);
                if (windowSize < TimeSpan.FromDays(options.MinWindowDays))
                    windowSize = TimeSpan.FromDays(options.MinWindowDays);
                continue;
            }

            if (ids.Count == 0)
                yield break;

            yield return new WorkItemQueryWindow
            {
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                WindowSize = windowSize,
                WorkItemIds = ids
            };

            // Grow window gradually after a narrow success
            if (windowSize < TimeSpan.FromDays(30))
                windowSize += TimeSpan.FromDays(1);

            windowEnd = windowStart;
        }
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''");
}
