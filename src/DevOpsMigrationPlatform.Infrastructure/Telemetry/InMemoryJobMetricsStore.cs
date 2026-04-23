#if !NETFRAMEWORK
using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Lock-free, single-value metrics store backed by a volatile reference.
/// Thread safety: volatile write/read is sufficient for the single-writer (SnapshotMetricExporter)
/// single-reader (ControlPlaneTelemetryTimer) pattern used here.
/// </summary>
internal sealed class InMemoryJobMetricsStore : DevOpsMigrationPlatform.Abstractions.IJobMetricsStore
{
    private volatile DevOpsMigrationPlatform.Abstractions.JobMetrics? _latest;

    public void Update(DevOpsMigrationPlatform.Abstractions.JobMetrics metrics) =>
        _latest = metrics;

    public DevOpsMigrationPlatform.Abstractions.JobMetrics? Latest => _latest;
}
#endif
