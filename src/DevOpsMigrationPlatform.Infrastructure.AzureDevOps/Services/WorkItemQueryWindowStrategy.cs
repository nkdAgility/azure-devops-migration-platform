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
        // NOTE: The ADO WIQL API throws (rather than silently truncating) when a
        // project exceeds the 20,000-item hard cap.  We catch that here and fall
        // through to the backward date-window scan (Step 2) so both inventory and
        // export remain correct for large projects.
        // yield cannot appear inside a try-catch in C#; the result is extracted
        // first and the yield statements remain outside the try-catch below.
        var unboundedWiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{EscapeWiql(project)}' " +
                    $"ORDER BY [System.Id]"
        };

        List<int>? unboundedIds = null;
        try
        {
            var unboundedResult = await witClient.QueryByWiqlAsync(unboundedWiql, project, cancellationToken);
            unboundedIds = unboundedResult.WorkItems.Select(r => r.Id).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // API threw — project likely exceeds the 20,000-item WIQL hard cap.
            // unboundedIds stays null; fall through to backward date-window scanning.
        }

        if (unboundedIds != null && unboundedIds.Count < options.LimitThreshold)
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
        //
        // KEY DESIGN NOTE: The ADO WIQL API throws VS402337 when a windowed query
        // would return > 20,000 items (it does NOT silently truncate).  Overflow
        // exceptions must halve the window WITHOUT consuming a transient-retry slot
        // so that very dense date ranges are handled correctly regardless of depth.
        var windowSize = TimeSpan.FromDays(options.InitialWindowDays);
        var windowEnd = DateTime.UtcNow;
        const int maxTransientRetries = 3;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windowStart = windowEnd - windowSize;
            List<int>? ids = null;
            int transientRetries = 0;

            // Inner loop: keeps retrying the same time range until we get a valid
            // result, halving on overflow (no retry-slot consumed) and retrying on
            // transient errors (retry-slot consumed each time).
            while (ids == null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Always rebuild the query from current windowStart/windowEnd so
                // that halving inside this loop is reflected correctly.
                var wiql = new Wiql
                {
                    Query = $"SELECT [System.Id] FROM WorkItems " +
                            $"WHERE [System.TeamProject] = '{EscapeWiql(project)}' " +
                            $"AND [System.CreatedDate] >= '{windowStart:yyyy-MM-dd}' " +
                            $"AND [System.CreatedDate] < '{windowEnd:yyyy-MM-dd}' " +
                            $"ORDER BY [System.Id]"
                };

                try
                {
                    var result = await witClient.QueryByWiqlAsync(wiql, project, cancellationToken);
                    var fetched = result.WorkItems.Select(r => r.Id).ToList();

                    if (fetched.Count >= options.LimitThreshold)
                    {
                        // API silently returned exactly at the cap (rare) — treat as overflow.
                        windowSize = HalveWindowSize(windowSize, options.MinWindowDays);
                        windowStart = windowEnd - windowSize;
                    }
                    else
                    {
                        ids = fetched; // success
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsOverflowException(ex))
                {
                    // VS402337 / TF201036: too many results — halve without consuming
                    // a transient-retry slot.  This is expected behaviour for large
                    // projects and must not be treated as an error.
                    var halved = HalveWindowSize(windowSize, options.MinWindowDays);
                    if (halved == windowSize)
                    {
                        // Already at the minimum window size and still overflowing.
                        // This means ≥ LimitThreshold items share the same CreatedDate
                        // (e.g. a bulk import of >20k items on one day).  We cannot
                        // shrink further, so surface a clear, actionable error instead
                        // of spinning in an infinite loop.
                        throw new InvalidOperationException(
                            $"The minimum window of {options.MinWindowDays} day(s) for project '{project}' " +
                            $"still exceeds the {options.LimitThreshold}-item WIQL limit. " +
                            $"Reduce MinWindowDays (currently {options.MinWindowDays}) so the window " +
                            $"can shrink below the dense period, or increase LimitThreshold.", ex);
                    }
                    windowSize = halved;
                    windowStart = windowEnd - windowSize;
                }
                catch (Exception) when (transientRetries < maxTransientRetries)
                {
                    // Genuine transient error — retry the same window, consume a slot.
                    transientRetries++;
                }
                // Any other exception (transient retries exhausted) propagates naturally.
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

            // Grow the window proportionally to spare capacity so that recovery
            // from a prior dense burst is fast:
            //   fill < 50% of limit → double (data is sparse; halving backstop
            //                                 reverts if the next window overflows)
            //   fill >= 50% of limit → keep current size (comfortable density)
            //
            // The empty-window path above already doubles for zero-result windows.
            // Overflow halving handles contraction from any window size.
            var fillRatio = (double)ids.Count / options.LimitThreshold;
            if (fillRatio < 0.5)
            {
                windowSize = windowSize * 2;
                if (windowSize > TimeSpan.FromDays(options.MaxWindowDays))
                    windowSize = TimeSpan.FromDays(options.MaxWindowDays);
            }
            // fillRatio >= 0.5: retain current window size.

            windowEnd = windowStart;
        }
    }

    /// <summary>
    /// Halves <paramref name="current"/>, flooring at <paramref name="minWindowDays"/>.
    /// </summary>
    private static TimeSpan HalveWindowSize(TimeSpan current, int minWindowDays)
    {
        var halved = TimeSpan.FromTicks(current.Ticks / 2);
        var min = TimeSpan.FromDays(minWindowDays);
        return halved < min ? min : halved;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> represents the ADO
    /// "too many results" hard cap (VS402337 / TF201036).
    /// </summary>
    private static bool IsOverflowException(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("VS402337", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TF201036", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("exceeds the size limit of 20000", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("exceeds the size limit of 20,000", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''");
}
