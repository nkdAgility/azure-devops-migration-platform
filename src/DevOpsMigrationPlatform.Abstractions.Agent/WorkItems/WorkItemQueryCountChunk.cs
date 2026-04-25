using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Intermediate count result produced while paging through all work items
/// using date-range chunking. Yielded by the count pass before the full export.
/// </summary>
public class WorkItemQueryCountChunk
{
    public int CurrentTotal { get; set; }
    public TimeSpan CurrentChunkTimespan { get; set; }
    public int CurrentChunkCount { get; set; }
}
