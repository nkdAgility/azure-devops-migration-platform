#if !NETFRAMEWORK

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Lock-free, single-value snapshot store backed by a volatile reference.
/// Thread safety: volatile write/read is sufficient for the single-writer (module)
/// single-reader (ControlPlaneTelemetryTimer) pattern used here.
/// </summary>
internal sealed class InMemoryJobSnapshotStore : DevOpsMigrationPlatform.Abstractions.IJobSnapshotStore
{
    private volatile DevOpsMigrationPlatform.Abstractions.JobSnapshot? _latest;

    public void Update(DevOpsMigrationPlatform.Abstractions.JobSnapshot snapshot) =>
        _latest = snapshot;

    public DevOpsMigrationPlatform.Abstractions.JobSnapshot? Latest => _latest;
}
#endif
