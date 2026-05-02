namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Atomic bootstrap response for late-joining clients.
/// Returned by <c>GET /jobs/{id}/bootstrap</c> as a single response containing
/// the current snapshot, metrics, and last event sequence.
/// </summary>
public record JobBootstrap
{
    /// <summary>
    /// Latest per-org/project snapshot. Null if the job has not yet emitted a snapshot.
    /// </summary>
    public JobSnapshot? Snapshot { get; init; }

    /// <summary>
    /// Latest aggregate metrics. Null if the job has not yet emitted metrics.
    /// </summary>
    public JobMetrics? Metrics { get; init; }

    /// <summary>
    /// The highest <see cref="ProgressEvent.EventSequence"/> emitted so far.
    /// Clients use this as the <c>Last-Event-ID</c> when subscribing to the SSE stream.
    /// Zero if no events have been emitted yet.
    /// </summary>
    public long LastEventSequence { get; init; }

    /// <summary>
    /// The agent's execution plan pushed at job start.
    /// Null if the agent has not yet pushed a task list.
    /// </summary>
    public JobTaskList? Tasks { get; init; }
}
