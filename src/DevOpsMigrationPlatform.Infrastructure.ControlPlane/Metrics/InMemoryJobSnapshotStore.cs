using DevOpsMigrationPlatform.Abstractions;
#if !NETFRAMEWORK

using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.ControlPlane.Metrics;

/// <summary>
/// Lock-free, single-value snapshot store backed by a volatile reference.
/// Thread safety: volatile write/read is sufficient for the single-writer (module)
/// single-reader (ControlPlaneTelemetryTimer) pattern used here.
/// Signals <see cref="UpdateSignal"/> on each <see cref="Update"/> so the
/// telemetry timer can wake immediately on project-boundary pushes.
/// </summary>
internal sealed class InMemoryJobSnapshotStore : IJobSnapshotStore
{
    private volatile JobSnapshot? _latest;
    private readonly ManualResetEventSlim _signal = new(false);

    public void Update(JobSnapshot snapshot)
    {
        _latest = snapshot;
        _signal.Set();
    }

    public JobSnapshot? Latest => _latest;

    public WaitHandle UpdateSignal => _signal.WaitHandle;

    /// <summary>Resets the signal after the timer has pushed the snapshot.</summary>
    internal void ResetSignal() => _signal.Reset();
}
#endif
