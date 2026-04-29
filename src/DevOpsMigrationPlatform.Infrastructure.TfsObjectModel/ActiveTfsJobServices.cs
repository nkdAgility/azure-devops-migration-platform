namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Singleton that carries the current job's <see cref="TfsJobServices"/> across services.
/// Set by <c>TfsJobAgentWorker</c> when a job lease is acquired and cleared on release.
/// Services that need TFS Object Model clients (e.g. <c>TfsClassificationTreeCapture</c>,
/// <c>TfsWorkItemRevisionSourceFactory</c>) read from this holder rather than taking
/// per-job services as constructor dependencies, because TFS services are only available
/// after a job is picked up and an endpoint is resolved.
/// Mirrors the <c>ActivePackageState</c> pattern from <c>DevOpsMigrationPlatform.Infrastructure.Agent</c>.
/// </summary>
public sealed class ActiveTfsJobServices
{
    private volatile TfsJobServices? _current;

    /// <summary>
    /// The <see cref="TfsJobServices"/> for the currently active job,
    /// or <c>null</c> if no job is active.
    /// Thread-safe: volatile write/read provides sufficient ordering for the
    /// single-writer multi-reader pattern used by the agent.
    /// </summary>
    public TfsJobServices? Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>
    /// Returns <see cref="Current"/> or throws <see cref="InvalidOperationException"/>
    /// when called outside of an active job context.
    /// </summary>
    public TfsJobServices Require()
        => _current ?? throw new InvalidOperationException(
            "No active TFS job services. Ensure this is called within an active job context.");

    /// <summary>Clears the active services reference (does not dispose).</summary>
    public void Clear() => _current = null;
}
