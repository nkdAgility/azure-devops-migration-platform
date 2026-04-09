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
/// Algorithm — three levels:
/// 1. Run one unbounded query (no date filter). For projects with fewer than
///    <see cref="WorkItemQueryWindowOptions.LimitThreshold"/> items this is
///    sufficient — one API call, one yielded window, done.
/// 2. If the project hits the WIQL cap (≥ LimitThreshold), fall back to
///    backward date-window scanning: halve on overflow, retry on WIQL error,
///    expand on empty windows until <see cref="WorkItemQueryWindowOptions.MinDate"/>.
/// 3. If a single-day window (Level 2 minimum) still overflows, page through
///    the dense day by <c>[System.Id]</c> range: <c>[System.Id] &gt; lower AND
///    [System.Id] &lt;= upper</c>, halving the range on overflow, probing for
///    remaining items when a range is empty.
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

        // Resolve WHERE predicate and ORDER BY: use the caller-supplied base query
        // (e.g. from the scenario configuration) or fall back to the default
        // project-scoped query.  This lets Export callers preserve custom WHERE
        // conditions and ORDER BY strategies while the window algorithm still
        // injects its date-range filters for large projects.
        string wherePredicate;
        string orderBy;
        if (options.BaseQuery is { Length: > 0 })
        {
            (wherePredicate, orderBy) = ParseBaseQuery(options.BaseQuery);
        }
        else
        {
            wherePredicate = $"[System.TeamProject] = '{EscapeWiql(project)}'";
            orderBy = "[System.Id]";
        }

        // ── Step 1: Unbounded probe ──────────────────────────────────────────
        // A single query without date filters retrieves all IDs for the project.
        // If the result is below the WIQL cap this is the only API call needed.
        // NOTE: The Azure DevOps WIQL API throws (rather than silently truncating) when a
        // project exceeds the 20,000-item hard cap.  We catch that here and fall
        // through to the backward date-window scan (Step 2) so both inventory and
        // export remain correct for large projects.
        // yield cannot appear inside a try-catch in C#; the result is extracted
        // first and the yield statements remain outside the try-catch below.
        var unboundedWiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems WHERE {wherePredicate} ORDER BY {orderBy}"
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
        // KEY DESIGN NOTE: The Azure DevOps WIQL API throws VS402337 when a windowed query
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
            bool levelTwoRequired = false;
            string capturedWindowWhere = string.Empty;

            // Inner loop: keeps retrying the same time range until we get a valid
            // result, halving on overflow (no retry-slot consumed) and retrying on
            // transient errors (retry-slot consumed each time).
            while (ids == null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Always rebuild the query from current windowStart/windowEnd so
                // that halving inside this loop is reflected correctly.
                var dateFilter = $"[System.CreatedDate] >= '{windowStart:yyyy-MM-dd}' " +
                                 $"AND [System.CreatedDate] < '{windowEnd:yyyy-MM-dd}'";
                var windowWhere = wherePredicate.Length > 0
                    ? $"{wherePredicate} AND {dateFilter}"
                    : dateFilter;
                capturedWindowWhere = windowWhere;
                var wiql = new Wiql
                {
                    Query = $"SELECT [System.Id] FROM WorkItems WHERE {windowWhere} ORDER BY {orderBy}"
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
                        // Fall through to Level 2 ID-cursor paging within this dense day.
                        levelTwoRequired = true;
                        ids = new List<int>(); // exit inner while (ids == null) loop
                    }
                    else
                    {
                        windowSize = halved;
                        windowStart = windowEnd - windowSize;
                    }
                }
                catch (Exception) when (transientRetries < maxTransientRetries)
                {
                    // Genuine transient error — retry the same window, consume a slot.
                    transientRetries++;
                }
                // Any other exception (transient retries exhausted) propagates naturally.
            }

            // ── Level 2: ID-cursor paging within a dense single day ─────────
            if (levelTwoRequired)
            {
                await foreach (var idWindow in EnumerateIdWindowsAsync(
                    witClient, project, capturedWindowWhere,
                    windowStart, windowEnd, windowSize, options, cancellationToken))
                {
                    yield return idWindow;
                }
                windowEnd = windowStart;
                if (windowStart <= options.MinDate)
                    yield break;
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
    /// Level 2: pages through a single dense day by <c>[System.Id]</c> range
    /// when the date window has been reduced to <see cref="WorkItemQueryWindowOptions.MinWindowDays"/>
    /// and still overflows.  Each yielded window carries the same date bounds.
    /// </summary>
    private async IAsyncEnumerable<WorkItemQueryWindow> EnumerateIdWindowsAsync(
        IWiqlQueryClient witClient,
        string project,
        string windowWhere,
        DateTime windowStart,
        DateTime windowEnd,
        TimeSpan windowSize,
        WorkItemQueryWindowOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int idCursor = 0;
        int idWindowSize = options.InitialIdWindowSize;
        const int maxTransientRetries = 3;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<int>? pageIds = null;
            bool dayExhausted = false;
            int transientRetries = 0;

            while (pageIds == null && !dayExhausted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int idUpper = idCursor + idWindowSize;
                var idFilter = $"[System.Id] > {idCursor} AND [System.Id] <= {idUpper}";
                var wiql = new Wiql
                {
                    Query = $"SELECT [System.Id] FROM WorkItems WHERE {windowWhere} AND {idFilter} ORDER BY [System.Id]"
                };

                try
                {
                    var result = await witClient.QueryByWiqlAsync(wiql, project, cancellationToken);
                    var fetched = result.WorkItems.Select(r => r.Id).ToList();

                    if (fetched.Count >= options.LimitThreshold)
                    {
                        // Still too many in this ID range — halve (floor at 1).
                        idWindowSize = Math.Max(1, idWindowSize / 2);
                    }
                    else if (fetched.Count == 0)
                    {
                        // Gap in IDs — probe for remaining items beyond this range.
                        (pageIds, dayExhausted, idWindowSize) = await ProbeRemainingAsync(
                            witClient, project, windowWhere, idUpper, options, cancellationToken);

                        if (!dayExhausted && pageIds == null)
                        {
                            // Probe found items exist further ahead but they overflow.
                            // Advance cursor past the gap and retry.
                            idCursor = idUpper;
                            continue;
                        }
                    }
                    else
                    {
                        pageIds = fetched;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsOverflowException(ex))
                {
                    idWindowSize = Math.Max(1, idWindowSize / 2);
                }
                catch (Exception) when (transientRetries < maxTransientRetries)
                {
                    transientRetries++;
                }
                // Transient retries exhausted → propagates naturally.
            }

            if (dayExhausted)
                yield break;

            if (pageIds!.Count > 0)
            {
                yield return new WorkItemQueryWindow
                {
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    WindowSize = windowSize,
                    WorkItemIds = pageIds
                };

                // Advance cursor to the highest ID returned.
                idCursor = pageIds[^1];

                // Adaptive sizing: grow if sparse, leave alone if moderate.
                var fillRatio = (double)pageIds.Count / options.LimitThreshold;
                if (fillRatio < 0.5)
                    idWindowSize = idWindowSize * 2;
            }
            else
            {
                // Empty page with no probe (shouldn't happen, but guard).
                yield break;
            }
        }
    }

    /// <summary>
    /// Probes whether any items exist beyond <paramref name="idFloor"/> within the
    /// current date-bounded <paramref name="windowWhere"/>. Used by Level 2 to skip
    /// over large gaps in the ID space without advancing one chunk at a time.
    /// </summary>
    /// <returns>
    /// A tuple of (pageIds, dayExhausted, newIdWindowSize):
    /// <list type="bullet">
    ///   <item>Probe empty → (null, true, unchanged) — day fully drained.</item>
    ///   <item>Probe fits → (probeIds, false, unchanged) — remaining items returned.</item>
    ///   <item>Probe overflows → (null, false, doubled) — caller advances cursor and retries.</item>
    /// </list>
    /// </returns>
    private static async Task<(List<int>? PageIds, bool DayExhausted, int NewIdWindowSize)> ProbeRemainingAsync(
        IWiqlQueryClient witClient,
        string project,
        string windowWhere,
        int idFloor,
        WorkItemQueryWindowOptions options,
        CancellationToken cancellationToken)
    {
        var probeWiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems WHERE {windowWhere} AND [System.Id] > {idFloor} ORDER BY [System.Id]"
        };

        try
        {
            var probeResult = await witClient.QueryByWiqlAsync(probeWiql, project, cancellationToken);
            var probeIds = probeResult.WorkItems.Select(r => r.Id).ToList();

            if (probeIds.Count == 0)
                return (null, true, options.InitialIdWindowSize);

            if (probeIds.Count < options.LimitThreshold)
                return (probeIds, false, options.InitialIdWindowSize);

            // Probe overflows — there are many items ahead but they don't fit in one
            // query. Signal caller to advance cursor and retry with doubled window.
            return (null, false, options.InitialIdWindowSize * 2);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsOverflowException(ex))
        {
            // Probe itself overflows — items exist ahead but too many for one query.
            return (null, false, options.InitialIdWindowSize * 2);
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
    /// Returns <see langword="true"/> when <paramref name="ex"/> represents the Azure DevOps
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

    /// <summary>
    /// Extracts the WHERE predicate and ORDER BY from a user-supplied WIQL query.
    /// The SELECT preamble is discarded; the strategy always projects [System.Id].
    /// If no ORDER BY is present, defaults to <c>[System.Id]</c>.
    /// </summary>
    private static (string WherePredicate, string OrderBy) ParseBaseQuery(string query)
    {
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;

        // Locate ORDER BY — search from the end to avoid false matches inside predicates.
        int orderByIdx = query.LastIndexOf("ORDER BY", ic);
        string orderBy = orderByIdx >= 0
            ? query[(orderByIdx + "ORDER BY".Length)..].Trim()
            : "[System.Id]";

        // Locate WHERE — the first occurrence after the FROM clause.
        int whereIdx = query.IndexOf(" WHERE ", ic);
        if (whereIdx < 0) whereIdx = query.IndexOf("\nWHERE ", ic);
        if (whereIdx < 0) whereIdx = query.IndexOf("\rWHERE ", ic);

        string wherePredicate;
        if (whereIdx >= 0)
        {
            int predicateStart = whereIdx + " WHERE ".Length;
            int predicateEnd = orderByIdx >= 0 ? orderByIdx : query.Length;
            wherePredicate = query[predicateStart..predicateEnd].Trim();
        }
        else
        {
            wherePredicate = string.Empty;
        }

        return (wherePredicate, orderBy);
    }
}
