using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// A date-range chunk used to page through work items in the source system.
/// Returned as part of <see cref="WorkItemMigrationProgress"/> during export.
/// </summary>
public class WorkItemQueryChunk
{
    public DateTime ChunkStart { get; set; }
    public DateTime ChunkEnd { get; set; }
    public TimeSpan ChunkSize { get; set; }
    public int QueryIndex { get; set; }
    public int WorkItemIndexInChunk { get; set; }
    public int WorkItemsInChunk { get; set; }
}
