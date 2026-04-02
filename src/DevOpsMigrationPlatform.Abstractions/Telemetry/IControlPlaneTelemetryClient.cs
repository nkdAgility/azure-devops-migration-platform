using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Posts a <see cref="MetricSnapshot"/> to the Control Plane on behalf of the Migration Agent.
/// Called on a periodic timer while a lease is held.
/// </summary>
public interface IControlPlaneTelemetryClient
{
    /// <summary>
    /// Pushes <paramref name="snapshot"/> to <c>POST /agents/lease/{leaseId}/telemetry</c>.
    /// Best-effort — implementations must not throw on transient failures or non-success responses.
    /// </summary>
    Task PushSnapshotAsync(string leaseId, MetricSnapshot snapshot, CancellationToken ct);
}
