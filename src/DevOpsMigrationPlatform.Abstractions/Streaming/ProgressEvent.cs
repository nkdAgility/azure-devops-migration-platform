using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using System;

namespace DevOpsMigrationPlatform.Abstractions.Streaming;

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
    /// <para>
    /// <strong>When to populate:</strong> Every module MUST set this on its completion event
    /// (e.g. <c>MyModule.Export.Complete</c>) with its aggregate counters.
    /// <c>ProgressController.PostProgress</c> reads this field and merges it into
    /// <c>JobMetricsStore</c>, which is the source for <c>GET /jobs/{id}/telemetry</c>
    /// — the only channel the CLI and TUI read for live counter display.
    /// </para>
    /// <para>
    /// <strong>TFS subprocess (net481):</strong> Also populated every N revisions
    /// (controlled by <see cref="TelemetryOptions.SubprocessSnapshotRevisionInterval"/>; default 100).
    /// </para>
    /// <para>
    /// <strong>Relationship to OTel instruments (<c>IMigrationMetrics</c>):</strong>
    /// OTel instruments flow to Azure Monitor / Application Insights only — they do NOT
    /// reach the CLI/TUI display. Both must be called for complete observability.
    /// </para>
    /// </summary>
    public JobMetrics? Metrics { get; init; }
}
