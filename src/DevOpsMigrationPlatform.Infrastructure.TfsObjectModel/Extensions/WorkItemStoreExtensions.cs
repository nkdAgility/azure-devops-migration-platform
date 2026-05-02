// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Models;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Extensions;

/// <summary>
/// Extension methods on <see cref="WorkItemStore"/> for date-range chunked querying.
/// Chunking prevents WIQL result sets exceeding the TFS 20,000-item limit.
/// </summary>
public static class WorkItemStoreExtensions
{
    /// <summary>
    /// Iterates over date-range chunks, yielding a running count of matching work items.
    /// Does NOT load <see cref="WorkItem"/> objects — count-only pass.
    /// </summary>
    public static IEnumerable<WorkItemQueryCountChunk> QueryCountAllByDateChunk(
        this WorkItemStore store,
        string baseQuery,
        IProgressSink progressSink,
        TimeSpan? initialChunkSize = null,
        int maxItemsPerQuery = 20000)
    {
        DateTime endDate = DateTime.UtcNow;
        TimeSpan chunkSize = initialChunkSize ?? TimeSpan.FromDays(120);
        var status = new WorkItemQueryCountChunk
        {
            CurrentTotal = 0,
            CurrentChunkTimespan = chunkSize,
            CurrentChunkCount = 0
        };

        while (true)
        {
            DateTime startDate = endDate - chunkSize;

            string wiql = $@"{baseQuery}
          AND [System.CreatedDate] >= '{startDate:yyyy-MM-dd}'
          AND [System.CreatedDate] < '{endDate:yyyy-MM-dd}'";

            try
            {
                var returnCount = store.QueryCount(wiql);
                status.CurrentTotal += returnCount;
                status.CurrentChunkCount = returnCount;
                status.CurrentChunkTimespan = chunkSize;

                if (returnCount >= maxItemsPerQuery)
                {
                    chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
                    continue;
                }

                if (returnCount == 0)
                    yield break;
            }
            catch (Exception ex)
            {
                progressSink.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = "Count",
                    Message = $"WIQL query failed: {ex.Message}, reducing chunk size to {chunkSize.TotalDays:F1} days"
                });
                chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
                continue;
            }

            yield return status;

            if (chunkSize < TimeSpan.FromDays(30))
                chunkSize += TimeSpan.FromDays(1);

            endDate = startDate;
        }
    }

    /// <summary>
    /// Iterates over date-range chunks, yielding individual work items one at a time.
    /// Always streams — never buffers the full result set.
    /// </summary>
    public static IEnumerable<WorkItemFromChunk> QueryAllByDateChunk(
        this WorkItemStore store,
        string baseQuery,
        IProgressSink progressSink,
        TimeSpan? initialChunkSize = null,
        int maxItemsPerQuery = 20000)
    {
        DateTime endDate = DateTime.UtcNow;
        TimeSpan chunkSize = initialChunkSize ?? TimeSpan.FromDays(120);
        int queryIndex = 0;

        while (true)
        {
            DateTime startDate = endDate - chunkSize;

            string wiql = $@"{baseQuery}
          AND [System.CreatedDate] >= '{startDate:yyyy-MM-dd}'
          AND [System.CreatedDate] < '{endDate:yyyy-MM-dd}'";

            WorkItemCollection workItems;
            try
            {
                workItems = store.Query(wiql);

                if (workItems.Count >= maxItemsPerQuery)
                {
                    chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
                    continue;
                }

                if (workItems.Count == 0)
                    yield break;
            }
            catch (Exception ex)
            {
                progressSink.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = "Query",
                    Message = $"WIQL query failed: {ex.Message}, reducing chunk size to {chunkSize.TotalDays:F1} days"
                });
                chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
                continue;
            }

            for (int i = 0; i < workItems.Count; i++)
            {
                yield return new WorkItemFromChunk
                {
                    WorkItem = workItems[i],
                    ChunkStart = startDate,
                    ChunkEnd = endDate,
                    ChunkSize = chunkSize,
                    QueryIndex = queryIndex,
                    WorkItemsInChunk = workItems.Count,
                    WorkItemIndexInChunk = i
                };
            }

            if (chunkSize < TimeSpan.FromDays(30))
                chunkSize += TimeSpan.FromDays(1);

            endDate = startDate;
            queryIndex++;
        }
    }
}
