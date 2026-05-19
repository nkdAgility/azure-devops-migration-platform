// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Globalization;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Singleton that carries active package runtime state across agent services.
/// Set by the worker when a job lease is acquired and cleared on release.
/// Services that persist data to the package (e.g. loggers, progress sinks) read from
/// this holder rather than taking <see cref="IArtefactStore"/> as a constructor dependency,
/// because the store is only available after a job is picked up and a package URI is resolved.
/// </summary>
public sealed class ActivePackageState
{
    private volatile Job? _currentJob;
    private volatile string? _cachedRunId;

    /// <summary>
    /// The currently active job, or <c>null</c> if no job is active.
    /// </summary>
    public Job? CurrentJob
    {
        get => _currentJob;
        set
        {
            var previous = _currentJob;
            _currentJob = value;

            if (!string.Equals(previous?.JobId, value?.JobId, StringComparison.Ordinal))
            {
                _cachedRunId = null;
            }
        }
    }

    /// <summary>
    /// Returns the unique run identifier for the current job: <c>yyyyMMdd-HHmmss</c>.
    /// Returns <c>null</c> when no job is active.
    /// The value is cached on first access so all services within a job share the same run folder.
    /// </summary>
    public string? CurrentRunId
    {
        get
        {
            var job = _currentJob;
            if (job is null)
                return null;

            return _cachedRunId ??= DateTimeOffset.UtcNow.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
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
                ? ".migration/Logs"
                : $".migration/runs/{runId}/logs";
        }
    }

    /// <summary>
    /// Clears all state when a job completes or the lease is released.
    /// </summary>
    public void Clear()
    {
        _currentJob = null;
        _cachedRunId = null;
    }
}
