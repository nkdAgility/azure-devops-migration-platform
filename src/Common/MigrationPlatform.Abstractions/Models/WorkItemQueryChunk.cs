namespace MigrationPlatform.Abstractions.Models
{
    public class WorkItemQueryChunk
    {
        public DateTime ChunkStart { get; set; }
        public DateTime ChunkEnd { get; set; }
        public TimeSpan ChunkSize { get; set; }
        public int QueryIndex { get; set; }
        public int WorkItemIndexInChunk { get; set; }
        public int WorkItemsInChunk { get; set; }
    }
}
