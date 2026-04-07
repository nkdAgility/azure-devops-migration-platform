using System;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Progress event emitted per completed date window during inventory.
/// Used by both the in-process ADO path and the TFS subprocess NDJSON path.
/// </summary>
public sealed class InventoryProgressEvent
{
    public string ProjectName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>Running total of work items counted so far for this project.</summary>
    public int WorkItemsCount { get; set; }
    /// <summary>Running total of revisions counted so far for this project.</summary>
    public int RevisionsCount { get; set; }
    /// <summary>Number of Git repositories in this project. Populated on the final event only.</summary>
    public int ReposCount { get; set; }
    /// <summary>True on the final event for this project.</summary>
    public bool IsComplete { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public TimeSpan WindowSize { get; set; }
    /// <summary>Non-null on error events.</summary>
    public string? Error { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
