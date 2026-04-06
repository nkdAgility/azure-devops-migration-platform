using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Options controlling the date-window algorithm.
/// </summary>
public sealed class WorkItemQueryWindowOptions
{
    public int InitialWindowDays { get; set; } = 120;
    public int LimitThreshold { get; set; } = 20_000;
    public int MinWindowDays { get; set; } = 1;
}

/// <summary>
/// One window result yielded by <see cref="WorkItemQueryWindowStrategy"/>.
/// </summary>
public sealed class WorkItemQueryWindow
{
    public DateTime WindowStart { get; init; }
    public DateTime WindowEnd { get; init; }
    public TimeSpan WindowSize { get; init; }
    public IReadOnlyList<int> WorkItemIds { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Shared date-window WIQL strategy that keeps each query under the 20,000-item limit.
/// Used by both <see cref="InventoryService"/> (counting) and
/// <see cref="CatalogService"/> (export paging).
/// </summary>
public sealed class WorkItemQueryWindowStrategy : IWorkItemQueryWindowStrategy
{
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

        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgOrCollection), credentials);
        var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

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
                catch (Exception) when (retries < maxRetries)
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
