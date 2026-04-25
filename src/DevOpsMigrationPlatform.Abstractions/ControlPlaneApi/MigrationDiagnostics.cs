namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// OTel-derived mean values and correctness counters for migration jobs.
/// Aggregate-only — always null at per-project scope in <see cref="ProjectSnapshot"/>.
/// Values correspond to registered OTel instruments in <see cref="WellKnownMetricNames"/>.
/// </summary>
public record MigrationDiagnostics
{
    // --- Duration ---

    /// <summary>Mean work item processing duration in milliseconds.</summary>
    public double? WorkItemDurationMeanMs { get; init; }

    // --- Payload / Complexity means ---

    /// <summary>Mean number of fields per work item.</summary>
    public double? FieldCountMean { get; init; }

    /// <summary>Mean number of attachments per work item.</summary>
    public double? AttachmentCountMean { get; init; }

    /// <summary>Mean number of links per work item.</summary>
    public double? LinkCountMean { get; init; }

    /// <summary>Mean number of revisions per work item.</summary>
    public double? RevisionCountMean { get; init; }

    /// <summary>Mean payload size in bytes per work item.</summary>
    public double? PayloadBytesMean { get; init; }

    // --- Correctness ---

    /// <summary>Revisions detected as missing in target.</summary>
    public long RevisionsMissing { get; init; }

    /// <summary>Revisions found in unexpected order.</summary>
    public long RevisionOrderErrors { get; init; }

    /// <summary>Links that could not be resolved.</summary>
    public long BrokenLinks { get; init; }

    /// <summary>Work items referenced but not found.</summary>
    public long MissingWorkItems { get; init; }

    // --- In-Flight gauges ---

    /// <summary>Work items currently being processed.</summary>
    public int WorkItemsInFlight { get; init; }

    /// <summary>Items queued for processing.</summary>
    public int QueueDepth { get; init; }
}
