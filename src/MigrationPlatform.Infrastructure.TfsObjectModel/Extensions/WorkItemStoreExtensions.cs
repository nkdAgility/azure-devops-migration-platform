using Microsoft.TeamFoundation.WorkItemTracking.Client;
using MigrationPlatform.Infrastructure.TfsObjectModel.Models;

namespace MigrationPlatform.Infrastructure.TfsObjectModel.Extensions
{
    public static class WorkItemStoreExtensions
    {
        public static IEnumerable<WorkItemFromChunk> QueryAllByDateChunk(
                                             this WorkItemStore store,
                                             string baseQuery,
                                             TimeSpan? initialChunkSize = null,
                                             int maxItemsPerQuery = 20000)
        {
            DateTime endDate = DateTime.UtcNow;
            TimeSpan chunkSize = initialChunkSize ?? TimeSpan.FromDays(30);
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
                    Console.WriteLine($"WIQL query failed: {ex.Message}");
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

}
