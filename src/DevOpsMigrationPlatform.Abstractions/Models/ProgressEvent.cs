using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// A structured progress event emitted by the Job Engine or a module.
/// Written to the package log and reported to the control plane.
/// In the TFS export subprocess, serialised as a single NDJSON line on stdout.
/// </summary>
public record ProgressEvent
{
    /// <summary>Module that emitted this event, e.g. "WorkItems".</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>Current stage label, e.g. "AppliedFields". Matches cursor stage values.</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Relative path of the last processed revision folder.</summary>
    public string? LastProcessed { get; init; }

    /// <summary>Total work items seen so far.</summary>
    public int TotalWorkItems { get; init; }

    /// <summary>Work items fully processed.</summary>
    public int WorkItemsProcessed { get; init; }

    /// <summary>Revisions written to the package.</summary>
    public int RevisionsProcessed { get; init; }

    /// <summary>Work item ID currently being processed.</summary>
    public int WorkItemId { get; init; }

    /// <summary>Human-readable status message.</summary>
    public string? Message { get; init; }

    /// <summary>UTC timestamp when this event was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional metric snapshot emitted alongside this progress event.
    /// Populated by the TFS subprocess every N revisions (controlled by
    /// <see cref="TelemetryOptions.SubprocessSnapshotRevisionInterval"/>; default 100).
    /// Null when emitted by the .NET 10 Migration Agent directly.
    /// </summary>
    public MetricSnapshot? Metrics { get; init; }
}
