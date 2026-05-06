// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Lease;

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
    private volatile string? _cachedRunId;

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
    /// </summary>
    public string? CurrentJobId
    {
        get => _currentJobId;
        set => _currentJobId = value;
    }

    /// <summary>
    /// Returns the unique run identifier for the current job: <c>{ticks}-{jobId}</c>.
    /// Returns <c>null</c> when no job is active.
    /// The value is cached on first access so all services within a job share the same run folder.
    /// </summary>
    public string? CurrentRunId
    {
        get
        {
            var jobId = _currentJobId;
            if (string.IsNullOrEmpty(jobId))
                return null;

            return _cachedRunId ??= PackagePaths.BuildRunId(DateTimeOffset.UtcNow.Ticks, jobId!);
        }
    }

    /// <summary>
    /// Returns the log folder for the current job,
    /// e.g. <c>.migration/runs/638807123456789012-a1b2c3d4/logs</c>.
    /// Falls back to <c>.migration/Logs</c> when no job is active.
    /// </summary>
    public string CurrentLogFolder
    {
        get
        {
            var runId = CurrentRunId;
            return runId is null
                ? PackagePaths.Logs
                : PackagePaths.RunLogsFolder(runId);
        }
    }

    /// <summary>
    /// Clears all state when a job completes or the lease is released.
    /// </summary>
    public void Clear()
    {
        _currentStore = null;
        _currentJobId = null;
        _cachedRunId = null;
    }
}
