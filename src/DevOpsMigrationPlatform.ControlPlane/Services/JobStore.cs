using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// In-memory store for submitted <see cref="MigrationJob"/> instances.
/// Queues pending jobs so agents can poll and acquire leases.
/// Thread-safe; suitable for single-node deployments.
/// </summary>
public sealed class JobStore
{
    private readonly ConcurrentDictionary<Guid, MigrationJob> _all = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly ConcurrentQueue<Guid> _pending = new();

    /// <summary>
    /// Stores a submitted job and enqueues it for agent pickup.
    /// Returns the job id.
    /// </summary>
    public Guid Enqueue(MigrationJob job)
    {
        var jobId = Guid.Parse(job.JobId);
        _all[jobId] = job;
        _pending.Enqueue(jobId);
        _pendingSignal.Release();
        return jobId;
    }

    /// <summary>
    /// Retrieves one pending job (waiting up to <paramref name="timeout"/>).
    /// Returns <c>null</c> if no job became available within the timeout.
    /// </summary>
    public async Task<MigrationJob?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _pendingSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            return null;

        if (_pending.TryDequeue(out var jobId) && _all.TryGetValue(jobId, out var job))
            return job;

        return null;
    }

    /// <summary>
    /// Returns a snapshot of all submitted jobs (for status queries).
    /// </summary>
    public IReadOnlyList<MigrationJob> GetAll()
    {
        var result = new List<MigrationJob>(_all.Values);
        return result;
    }

    /// <summary>
    /// Returns the job with the given id, or <c>null</c> if not found.
    /// </summary>
    public MigrationJob? Get(Guid jobId) =>
        _all.TryGetValue(jobId, out var job) ? job : null;
}
