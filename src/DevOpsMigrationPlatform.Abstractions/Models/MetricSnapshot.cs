using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Point-in-time metric aggregates for a running export job.
/// Serialised as part of ProgressEvent when emitted from the TFS subprocess.
/// Also posted directly from the Migration Agent to the Control Plane.
/// </summary>
public record MetricSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // --- Work item counts ---
    public long WorkItemsExported    { get; init; }
    public long RevisionsExported    { get; init; }
    public long RevisionErrors       { get; init; }
    public long LinksExported        { get; init; }
    public long LinkErrors           { get; init; }

    // --- Attachment counts ---
    public long AttachmentsAttempted { get; init; }
    public long AttachmentsSucceeded { get; init; }
    public long AttachmentsFailed    { get; init; }

    // --- Duration aggregates (milliseconds, null until first measurement) ---
    public double? WorkItemDurationMeanMs  { get; init; }
    public double? RevisionDurationMeanMs  { get; init; }
    public double? TotalExportDurationMs   { get; init; }
}
