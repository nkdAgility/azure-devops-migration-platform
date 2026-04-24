namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Counters for work item processing within a migration job.
/// Used by <see cref="MigrationCounters"/> at both aggregate and per-project scope.
/// </summary>
public record WorkItemCounters
{
    /// <summary>Work items that entered the processing pipeline.</summary>
    public long Attempted { get; init; }

    /// <summary>Work items successfully completed.</summary>
    public long Completed { get; init; }

    /// <summary>Work items that failed permanently.</summary>
    public long Failed { get; init; }

    /// <summary>Work items skipped (e.g. already processed, filtered out).</summary>
    public long Skipped { get; init; }

    /// <summary>Total revisions processed across all work items.</summary>
    public long RevisionsProcessed { get; init; }

    /// <summary>
    /// Duration in milliseconds for the most recently completed work item.
    /// Used to detect back-off / throttling: a sudden spike indicates server-side rate limiting.
    /// </summary>
    public double LastWorkItemDurationMs { get; init; }

    /// <summary>
    /// Rolling average duration in milliseconds per completed work item.
    /// Baseline to compare against <see cref="LastWorkItemDurationMs"/>.
    /// </summary>
    public double AverageWorkItemDurationMs { get; init; }

    /// <summary>Optional attachment counters. Null when no attachments have been processed.</summary>
    public AttachmentCounters? Attachments { get; init; }
}
