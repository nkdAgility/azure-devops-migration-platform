#if !NETFRAMEWORK
using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Lock-free, single-value snapshot store backed by a volatile reference.
/// Thread safety: volatile write/read is sufficient for the single-writer (SnapshotMetricExporter)
/// single-reader (ControlPlaneTelemetryTimer) pattern used here.
/// </summary>
internal sealed class InMemoryMetricSnapshotStore : DevOpsMigrationPlatform.Abstractions.IMetricSnapshotStore
{
    private volatile DevOpsMigrationPlatform.Abstractions.MetricSnapshot? _latest;

    public void Update(DevOpsMigrationPlatform.Abstractions.MetricSnapshot snapshot) =>
        _latest = snapshot;

    public DevOpsMigrationPlatform.Abstractions.MetricSnapshot? Latest => _latest;
}
#endif
