using System;

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
    private volatile string? _currentJobId;
    private volatile string? _cachedLogFolder;

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

    /// <summary>
    /// The job ID of the currently active job, or <c>null</c> if no job is active.
    /// Used by log sinks to build job-scoped log folder paths
    /// (e.g. <c>Logs/&lt;ticks&gt;-&lt;jobId&gt;/</c>).
    /// </summary>
    public string? CurrentJobId
    {
        get => _currentJobId;
        set => _currentJobId = value;
    }

    /// <summary>
    /// Returns the log folder prefix for the current job, e.g. <c>.migration/Logs/638807123456789012-a1b2c3d4</c>.
    /// Falls back to <c>.migration/Logs</c> when no job is active.
    /// The folder name is cached for the lifetime of the job so all sinks write to the same folder.
    /// </summary>
    public string CurrentLogFolder
    {
        get
        {
            var jobId = _currentJobId;
            if (string.IsNullOrEmpty(jobId))
                return PackagePaths.Logs;

            return _cachedLogFolder ??= PackagePaths.JobLogFolder(DateTimeOffset.UtcNow.Ticks, jobId!);
        }
    }

    /// <summary>
    /// Clears all state when a job completes or the lease is released.
    /// </summary>
    public void Clear()
    {
        _currentStore = null;
        _currentJobId = null;
        _cachedLogFolder = null;
    }
}
