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
///
/// Algorithm:
/// 1. Run one unbounded query (no date filter). For projects with fewer than
///    <see cref="WorkItemQueryWindowOptions.LimitThreshold"/> items this is
///    sufficient — one API call, one yielded window, done.
/// 2. If the project hits the WIQL cap (≥ LimitThreshold), fall back to
///    backward date-window scanning: halve on overflow, retry on WIQL error,
///    expand on empty windows until <see cref="WorkItemQueryWindowOptions.MinDate"/>.
/// </summary>
public sealed class WorkItemQueryWindowStrategy : IWorkItemQueryWindowStrategy
{
    private readonly IWiqlQueryClientFactory _clientFactory;

    public WorkItemQueryWindowStrategy(IWiqlQueryClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async IAsyncEnumerable<WorkItemQueryWindow> EnumerateWindowsAsync(
        string organisationUrl,
        string project,
        string pat,
        WorkItemQueryWindowOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new WorkItemQueryWindowOptions();

        var witClient = await _clientFactory.CreateAsync(organisationUrl, pat, cancellationToken);

        // ── Step 1: Unbounded probe ──────────────────────────────────────────
        // A single query without date filters retrieves all IDs for the project.
        // If the result is below the WIQL cap this is the only API call needed.
        var unboundedWiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{EscapeWiql(project)}' " +
                    $"ORDER BY [System.Id]"
        };

        var unboundedResult = await witClient.QueryByWiqlAsync(unboundedWiql, project, cancellationToken);
        var unboundedIds = unboundedResult.WorkItems.Select(r => r.Id).ToList();

        if (unboundedIds.Count < options.LimitThreshold)
        {
            if (unboundedIds.Count > 0)
            {
                var now = DateTime.UtcNow;
                yield return new WorkItemQueryWindow
                {
                    WindowStart = options.MinDate,
                    WindowEnd = now,
                    WindowSize = now - options.MinDate,
                    WorkItemIds = unboundedIds
                };
            }
            yield break;
        }

        // ── Step 2: Date-window fallback (>= LimitThreshold items) ──────────
        // Walk backward through date windows, halving on overflow, expanding on
        // empty gaps, stopping at MinDate.
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
                    var result = await witClient.QueryByWiqlAsync(wiql, project, cancellationToken);
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
                windowSize = TimeSpan.FromTicks(windowSize.Ticks / 2);
                if (windowSize < TimeSpan.FromDays(options.MinWindowDays))
                    windowSize = TimeSpan.FromDays(options.MinWindowDays);
                continue;
            }

            if (ids.Count == 0)
            {
                if (windowStart <= options.MinDate)
                    yield break;

                windowSize = windowSize * 2;
                if (windowSize > TimeSpan.FromDays(options.MaxWindowDays))
                    windowSize = TimeSpan.FromDays(options.MaxWindowDays);
                windowEnd = windowStart;
                continue;
            }

            yield return new WorkItemQueryWindow
            {
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                WindowSize = windowSize,
                WorkItemIds = ids
            };

            if (windowSize < TimeSpan.FromDays(30))
                windowSize += TimeSpan.FromDays(1);

            windowEnd = windowStart;
        }
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''");
}
