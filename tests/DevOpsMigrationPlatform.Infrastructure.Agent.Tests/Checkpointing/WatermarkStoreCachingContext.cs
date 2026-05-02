// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Shared scenario state and mocks for Work Item Watermark Store step definitions.
/// Uses an in-memory dictionary to simulate the IIdMapStore watermark behaviour
/// (scenarios 1-6, 9-10) and a separate dictionary for WIQL query count caching
/// (scenarios 7-8).
/// </summary>
public class WatermarkStoreCachingContext
{
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);

    /// <summary>In-memory watermark dictionary (sourceId → lastRevisionIndex) with MAX semantics.</summary>
    public Dictionary<int, int> Watermarks { get; } = new();

    /// <summary>In-memory WIQL query count cache (queryKey → count).</summary>
    public Dictionary<string, int> WiqlCountCache { get; } = new();

    /// <summary>Tracks how many times the underlying WIQL data source was called per query key.</summary>
    public Dictionary<string, int> WiqlDataSourceCallCounts { get; } = new();

    /// <summary>Configured data-source return values per query key.</summary>
    public Dictionary<string, int> WiqlDataSourceReturns { get; } = new();

    /// <summary>Saved watermark state used to simulate persistence across restart.</summary>
    public Dictionary<int, int>? SavedWatermarks { get; set; }

    /// <summary>Simulates querying the data source for a WIQL query count.</summary>
    public int QueryDataSource(string queryKey)
    {
        WiqlDataSourceCallCounts.TryGetValue(queryKey, out var count);
        WiqlDataSourceCallCounts[queryKey] = count + 1;
        return WiqlDataSourceReturns.TryGetValue(queryKey, out var val) ? val : 0;
    }

    /// <summary>
    /// Checks the count for a query, using the cache if available,
    /// otherwise querying the data source and caching the result.
    /// </summary>
    public int GetCachedCountOrQuery(string queryKey)
    {
        if (WiqlCountCache.TryGetValue(queryKey, out var cached))
            return cached;

        var result = QueryDataSource(queryKey);
        WiqlCountCache[queryKey] = result;
        return result;
    }

    /// <summary>Records a new count for a query, replacing any cached value.</summary>
    public void RecordCount(string queryKey, int count)
    {
        WiqlCountCache[queryKey] = count;
    }

    public WatermarkStoreCachingContext()
    {
        SetupIdMapStoreMocks();
    }

    private void SetupIdMapStoreMocks()
    {
        MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                Watermarks.TryGetValue(id, out var val) ? (int?)val : null);
        MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((wiId, revIdx, _) =>
            {
                // MAX semantics: only update if new value is greater
                if (!Watermarks.TryGetValue(wiId, out var cur) || revIdx > cur)
                    Watermarks[wiId] = revIdx;
            })
            .Returns(Task.CompletedTask);
        MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }
}
