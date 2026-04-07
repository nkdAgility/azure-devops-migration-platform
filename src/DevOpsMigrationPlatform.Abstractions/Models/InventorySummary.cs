using System;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Per-project result record written to <c>discovery-summary.csv</c> on completion.
/// All projects are written, including those that failed mid-count.
/// </summary>
public sealed class InventorySummary
{
    public string Url { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public int WorkItemsCount { get; set; }
    public int RevisionsCount { get; set; }
    public int ReposCount { get; set; }
    public int PipelinesCount { get; set; }
    /// <summary>True when all date windows were scanned without error.</summary>
    public bool IsComplete { get; set; }
    /// <summary>Non-null when counting failed for this project.</summary>
    public string? Error { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
