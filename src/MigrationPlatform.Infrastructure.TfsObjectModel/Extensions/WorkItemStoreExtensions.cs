using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace MigrationPlatform.Infrastructure.TfsObjectModel.Extensions
{
    public static class WorkItemStoreExtensions
    {
        public static IEnumerable<WorkItem> QueryAllByDateChunk(
            this WorkItemStore store,
            string project,
            TimeSpan? initialChunkSize = null,
            int maxItemsPerQuery = 20000)
        {
            DateTime endDate = DateTime.UtcNow;
            TimeSpan chunkSize = initialChunkSize ?? TimeSpan.FromDays(30);

            while (true)
            {
                DateTime startDate = endDate - chunkSize;

                string wiql = $@"
                SELECT * 
                FROM WorkItems 
                WHERE [System.TeamProject] = '{project}' 
                  AND [System.ChangedDate] >= '{startDate:u}' 
                  AND [System.ChangedDate] < '{endDate:u}'";

                WorkItemCollection workItems;
                try
                {
                    workItems = store.Query(wiql);

                    if (workItems.Count >= maxItemsPerQuery)
                    {
                        // Too many results — reduce range and retry
                        chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
                        continue;
                    }

                    if (workItems.Count == 0)
                    {
                        // No more data — end
                        yield break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WIQL query failed: {ex.Message}");
                    chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
                    continue;
                }

                foreach (WorkItem workItem in workItems)
                {
                    yield return workItem;
                }

                // Optional: Grow range again
                if (chunkSize < TimeSpan.FromDays(30))
                    chunkSize += TimeSpan.FromDays(1);

                endDate = startDate;
            }
        }
    }

}
