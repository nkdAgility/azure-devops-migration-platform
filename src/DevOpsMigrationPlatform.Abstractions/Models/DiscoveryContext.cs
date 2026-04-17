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
}
