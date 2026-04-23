namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Counters for attachment processing within a migration job.
/// Shared by <see cref="WorkItemCounters"/> (nested) and used at both
/// aggregate (<see cref="JobMetrics"/>) and per-project (<see cref="ProjectSnapshot"/>) scope.
/// </summary>
public record AttachmentCounters
{
    /// <summary>Attachments successfully processed.</summary>
    public long Processed { get; init; }

    /// <summary>Attachment downloads or uploads that failed.</summary>
    public long Failed { get; init; }

    /// <summary>Total bytes transferred for attachments.</summary>
    public long TotalBytes { get; init; }
}
