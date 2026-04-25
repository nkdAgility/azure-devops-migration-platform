using DevOpsMigrationPlatform.Abstractions.Jobs;
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Context passed to <see cref="IDiscoveryModule.RunAsync"/>.
/// Provides the job definition and all required stores — modules must not access
/// the filesystem or source systems directly.
/// </summary>
public class DiscoveryContext
{
    /// <summary>The discovery job definition.</summary>
    public DiscoveryJob Job { get; init; } = null!;

    /// <summary>Output store for discovery results (CSVs, logs).</summary>
    public IArtefactStore ArtefactStore { get; init; } = null!;

    /// <summary>Cursor-based checkpoint store. Persists progress across restarts.</summary>
    public IStateStore StateStore { get; init; } = null!;

    /// <summary>Progress event sink. Emits structured events to the control plane ring buffer and package log.</summary>
    public IProgressSink ProgressSink { get; init; } = null!;

    /// <summary>
    /// Optional metrics store. When provided, discovery modules push aggregate
    /// <see cref="JobMetrics"/> so the Control Plane telemetry snapshot (Channel 2)
    /// carries discovery counters alongside migration counters.
    /// Null when running inside the TFS subprocess or unit tests.
    /// </summary>
    public IJobMetricsStore? MetricsStore { get; init; }

    /// <summary>
    /// Optional snapshot store (Channel 3). When provided, discovery modules push
    /// <see cref="JobSnapshot"/> with per-org/project state for late-joining clients.
    /// Null when running inside the TFS subprocess or unit tests.
    /// </summary>
    public IJobSnapshotStore? SnapshotStore { get; init; }
}
