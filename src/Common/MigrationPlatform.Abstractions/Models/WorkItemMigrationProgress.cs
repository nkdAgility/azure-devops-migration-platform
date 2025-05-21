namespace MigrationPlatform.Abstractions.Models
{
    public class WorkItemMigrationProgress
    {
        public int WorkItemId { get; set; }
        public int RevisionIndex { get; set; }

        public int AttachmentsProcessed { get; set; }
        public int LinksProcessed { get; set; }
        public int FieldsProcessed { get; set; }

        public int TotalWorkItems { get; set; }
        public int WorkItemsProcessed { get; set; }

        public int TotalRevisions { get; set; }
        public int RevisionsProcessed { get; set; }

        public string? Message { get; set; }  // For human-readable updates

        // Optional: Add a timestamp for observability
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public WorkItemQueryChunk? ChunkInfo { get; set; }
        public int AttachmentsFailed { get; set; }
    }

}
