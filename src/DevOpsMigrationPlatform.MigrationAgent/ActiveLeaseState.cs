namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Singleton that tracks the agent's currently-held lease identifier.
/// Set by <see cref="MigrationAgentWorker"/> when a lease is acquired and cleared on release.
/// Read by <see cref="ControlPlaneTelemetryTimer"/> to include the lease id in push requests.
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
