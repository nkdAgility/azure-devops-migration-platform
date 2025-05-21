
namespace MigrationPlatform.Abstractions.Models
{
    public class WorkItemQueryCountChunk
    {
        public int CurrentTotal { get; set; }
        public TimeSpan CurrentChunkTimespan { get; set; }
        public int CurrentChunkCount { get; set; }
    }
}
