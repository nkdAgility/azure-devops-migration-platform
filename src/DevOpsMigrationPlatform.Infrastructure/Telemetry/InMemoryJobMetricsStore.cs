using DevOpsMigrationPlatform.Abstractions;
#if !NETFRAMEWORK
using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Lock-free, single-value metrics store backed by a volatile reference.
/// Thread safety: volatile write/read is sufficient for the single-writer (SnapshotMetricExporter)
/// single-reader (ControlPlaneTelemetryTimer) pattern used here.
/// </summary>
public sealed class InMemoryJobMetricsStore : IJobMetricsStore
{
    private volatile JobMetrics? _latest;

    public void Update(JobMetrics metrics) =>
        _latest = metrics;

    public JobMetrics? Latest => _latest;
}
#endif
