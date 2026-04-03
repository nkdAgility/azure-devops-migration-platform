namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Singleton that carries the agent's currently-held lease identifier across services.
/// Set by the worker when a lease is acquired and cleared on release.
/// </summary>
public sealed class ActiveLeaseState
{
    private volatile string? _currentLeaseId;

    /// <summary>
    /// The currently active lease id, or <c>null</c> if no lease is held.
    /// Thread-safe: volatile write/read provides sufficient ordering for this
    /// single-writer single-reader pattern.
    /// </summary>
    public string? CurrentLeaseId
    {
        get => _currentLeaseId;
        set => _currentLeaseId = value;
    }
}
