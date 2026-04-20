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

    /// <summary>Returns the latest <see cref="MetricSnapshot"/> for a job, or <c>null</c> when none pushed yet.</summary>
    Task<MetricSnapshot?> GetTelemetryAsync(Guid jobId, CancellationToken ct);

    /// <summary>Streams live <see cref="ProgressEvent"/> records via SSE.</summary>
    IAsyncEnumerable<ProgressEvent> FollowLogsAsync(Guid jobId, CancellationToken ct);

    /// <summary>Streams live <see cref="DiagnosticLogRecord"/> records via SSE.</summary>
    IAsyncEnumerable<DiagnosticLogRecord> StreamDiagnosticsAsync(Guid jobId, string? level, CancellationToken ct);

    /// <summary>
    /// Returns <see langword="true"/> if the agent instance with <paramref name="agentInstanceId"/>
    /// is known and active via <c>GET /agents/{agentInstanceId}/status</c>.
    /// Returns <see langword="false"/> for 404, any non-2xx response, or network errors.
    /// Used by <c>PackageLockFileService</c> to detect stale locks.
    /// </summary>
    Task<bool> IsAgentActiveAsync(string agentInstanceId, CancellationToken ct);
}
