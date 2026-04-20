using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Point-in-time metric aggregates for a running migration job.
/// Serialised as part of ProgressEvent when emitted from the TFS subprocess.
/// Also posted directly from the Migration Agent to the Control Plane.
/// Properties correspond to registered OTel instruments in <see cref="WellKnownMetricNames"/>.
/// </summary>
public record MetricSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // --- Execution counters ---
    public long WorkItemsAttempted { get; init; }
    public long WorkItemsCompleted { get; init; }
    public long WorkItemsFailed { get; init; }
    public long WorkItemsRetried { get; init; }

    // --- Execution duration ---
    public double? WorkItemDurationMeanMs { get; init; }

    // --- Payload / Complexity means ---
    public double? FieldCountMean { get; init; }
    public double? AttachmentCountMean { get; init; }
    public double? LinkCountMean { get; init; }
    public double? RevisionCountMean { get; init; }
    public double? PayloadBytesMean { get; init; }

    // --- Correctness (Tier 3) ---
    public double? RevisionSourceCountMean { get; init; }
    public double? RevisionTargetCountMean { get; init; }
    public double? RevisionDeltaMean { get; init; }
    public long RevisionsMissing { get; init; }
    public long RevisionOrderErrors { get; init; }
    public long BrokenLinks { get; init; }
    public long MissingWorkItems { get; init; }

    // --- In-Flight ---
    public int WorkItemsInFlight { get; init; }
    public int QueueDepth { get; init; }

    // --- Idempotency (deferred — nullable until mapping store available) ---
    public long? Duplicated { get; init; }
    public long? ChangedOnRerun { get; init; }
    public long? ReprocessedAfterResume { get; init; }
    public long? DuplicatedAfterResume { get; init; }
    public long? MissingAfterResume { get; init; }
}
