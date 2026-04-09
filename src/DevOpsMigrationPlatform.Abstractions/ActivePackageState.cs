namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Singleton that carries the agent's current <see cref="IArtefactStore"/> across services.
/// Set by the worker when a job lease is acquired and cleared on release.
/// Services that persist data to the package (e.g. loggers, progress sinks) read from
/// this holder rather than taking <see cref="IArtefactStore"/> as a constructor dependency,
/// because the store is only available after a job is picked up and a package URI is resolved.
/// </summary>
public sealed class ActivePackageState
{
    private volatile IArtefactStore? _currentStore;

    /// <summary>
    /// The <see cref="IArtefactStore"/> for the currently active job's package,
    /// or <c>null</c> if no job is active.
    /// Thread-safe: volatile write/read provides sufficient ordering for the
    /// single-writer multi-reader pattern used by the agent.
    /// </summary>
    public IArtefactStore? CurrentStore
    {
        get => _currentStore;
        set => _currentStore = value;
    }
}
