using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Posts <see cref="JobMetrics"/> and <see cref="JobSnapshot"/> to the Control Plane
/// on behalf of the Migration Agent. Called on periodic timers while a lease is held.
/// </summary>
public interface IControlPlaneTelemetryClient
{
    /// <summary>
    /// Pushes <paramref name="metrics"/> to <c>POST /agents/lease/{leaseId}/metrics</c>.
    /// Best-effort — implementations must not throw on transient failures or non-success responses.
    /// </summary>
    Task PushMetricsAsync(string leaseId, JobMetrics metrics, CancellationToken ct);

    /// <summary>
    /// Pushes <paramref name="snapshot"/> to <c>POST /agents/lease/{leaseId}/snapshot</c>.
    /// Best-effort — implementations must not throw on transient failures or non-success responses.
    /// </summary>
    Task PushSnapshotAsync(string leaseId, JobSnapshot snapshot, CancellationToken ct);
}
