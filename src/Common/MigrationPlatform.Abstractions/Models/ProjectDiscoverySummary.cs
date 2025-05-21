namespace MigrationPlatform.Abstractions.Models
{
    public class ProjectDiscoverySummary
    {
        public string ProjectName { get; set; } = string.Empty;

        public int WorkItemsCount { get; set; }
        public int RevisionsCount { get; set; }
        public int ReposCount { get; set; }
        public int PipelinesCount { get; set; }

        public bool IsWorkItemComplete { get; set; }
        public bool IsRepoComplete { get; set; }
        public bool IsPipelineComplete { get; set; }


        // Optional: Timestamp or version
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }

}
