using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// A structured progress event (Channel 1: JobEvent) emitted by the Job Engine or a module.
/// Pure envelope — carries no counter fields. Maps to an OTel Event: something happened at a point in time.
/// Written to the package log and reported to the control plane via SSE fan-out.
/// In the TFS export subprocess, serialised as a single NDJSON line on stdout.
/// </summary>
public record ProgressEvent
{
    /// <summary>Module that emitted this event, e.g. "WorkItems".</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>Current stage label, e.g. "AppliedFields". Matches cursor stage values.</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Human-readable status message.</summary>
    public string? Message { get; init; }

    /// <summary>UTC timestamp when this event was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Monotonic event sequence number, assigned by the agent and scoped per job.
    /// Used as the SSE <c>id:</c> field for <c>Last-Event-ID</c> reconnect semantics.
    /// Client-side reducers discard events with sequence ≤ last applied.
    /// </summary>
    public long EventSequence { get; init; }

    /// <summary>UTC timestamp of the most recent checkpoint save. Null if no checkpoint has been written yet.</summary>
    public DateTimeOffset? LastCheckpointAt { get; init; }

    /// <summary>Estimated UTC time when the next checkpoint will be written. Null when checkpointing is per-item (always safe).</summary>
    public DateTimeOffset? NextCheckpointDueAt { get; init; }

    /// <summary>
    /// Optional <see cref="JobMetrics"/> emitted alongside this progress event.
    /// Populated by the TFS subprocess every N revisions (controlled by
    /// <see cref="TelemetryOptions.SubprocessSnapshotRevisionInterval"/>; default 100).
    /// Null when emitted by the .NET 10 Migration Agent directly (metrics are pushed
    /// via a separate HTTP channel in that case).
    /// </summary>
    public JobMetrics? Metrics { get; init; }
}
