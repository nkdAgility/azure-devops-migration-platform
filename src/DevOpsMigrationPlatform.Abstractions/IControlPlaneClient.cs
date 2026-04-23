using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

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
    /// Checks whether the agent with <paramref name="agentInstanceId"/> is currently active.
    /// Used by <c>PackageLockFileService</c> to distinguish live locks from stale locks.
    /// Returns <see langword="false"/> if the status cannot be determined.
    /// </summary>
    Task<bool> IsAgentActiveAsync(string agentInstanceId, CancellationToken ct);

    /// <summary>
    /// Returns the bootstrap payload for a job (snapshot + metrics + last event sequence),
    /// or <c>null</c> when the job has not yet emitted any telemetry.
    /// Calls <c>GET /jobs/{jobId}/bootstrap</c>.
    /// </summary>
    Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct);
}
