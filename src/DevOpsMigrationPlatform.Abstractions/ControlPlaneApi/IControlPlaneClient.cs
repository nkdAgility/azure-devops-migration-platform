using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Abstraction over the control-plane HTTP client.
/// Declared in Abstractions so TUI view unit tests can inject a fake implementation.
/// </summary>
public interface IControlPlaneClient
{
    /// <summary>Returns all jobs visible to the caller via <c>GET /jobs</c>.</summary>
    Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct);

    /// <summary>Returns the latest <see cref="JobMetrics"/> for a job, or <c>null</c> when none pushed yet.</summary>
    Task<JobMetrics?> GetTelemetryAsync(Guid jobId, CancellationToken ct);

    /// <summary>Streams live <see cref="ProgressEvent"/> records via SSE.</summary>
    IAsyncEnumerable<ProgressEvent> FollowLogsAsync(Guid jobId, CancellationToken ct, long? lastEventSequence = null);

    /// <summary>Streams live <see cref="DiagnosticLogRecord"/> records via SSE.</summary>
    IAsyncEnumerable<DiagnosticLogRecord> StreamDiagnosticsAsync(Guid jobId, string? level, CancellationToken ct);

    /// <summary>
    /// Returns the bootstrap payload for a job (snapshot + metrics + last event sequence),
    /// or <c>null</c> when the job has not yet emitted any telemetry.
    /// Calls <c>GET /jobs/{jobId}/bootstrap</c>.
    /// </summary>
    Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// Returns the current <see cref="JobTaskList"/> for a job, or <c>null</c> when the
    /// agent has not yet pushed an execution plan.
    /// Calls <c>GET /jobs/{jobId}/tasks</c>.
    /// </summary>
    Task<JobTaskList?> GetTasksAsync(Guid jobId, CancellationToken ct);
}
