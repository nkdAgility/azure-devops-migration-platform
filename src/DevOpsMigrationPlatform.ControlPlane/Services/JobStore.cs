using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Models;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// In-memory store for submitted <see cref="Job"/> instances (migration and discovery).
/// Queues pending jobs so agents can poll and acquire leases.
/// Thread-safe; suitable for single-node deployments.
/// </summary>
public sealed class JobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, Job> _all = new();
    private readonly ConcurrentDictionary<Guid, string> _states = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _submittedAt = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly ConcurrentQueue<Guid> _pending = new();

    /// <inheritdoc />
    public Guid Enqueue(Job job)
    {
        var jobId = Guid.Parse(job.JobId);
        _all[jobId] = job;
        _states[jobId] = "Queued";
        _submittedAt[jobId] = DateTimeOffset.UtcNow;
        _pending.Enqueue(jobId);
        _pendingSignal.Release();
        return jobId;
    }

    /// <inheritdoc />
    public async Task<Job?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _pendingSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            return null;

        if (_pending.TryDequeue(out var jobId) && _all.TryGetValue(jobId, out var job))
            return job;

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<Job> GetAll()
    {
        var result = new List<Job>(_all.Values);
        return result;
    }

    /// <inheritdoc />
    public Job? Get(Guid jobId) =>
        _all.TryGetValue(jobId, out var job) ? job : null;

    /// <inheritdoc />
    public IReadOnlyList<JobRecord> GetAllRecords()
    {
        var result = new List<JobRecord>(_all.Count);
        foreach (var (jobId, job) in _all)
        {
            var state = _states.TryGetValue(jobId, out var s) ? s : "Queued";
            var submittedAt = _submittedAt.TryGetValue(jobId, out var t) ? t : DateTimeOffset.UtcNow;
            result.Add(new JobRecord(job, state, string.Empty, submittedAt));
        }
        return result;
    }

    /// <inheritdoc />
    public void SetState(Guid jobId, string state) =>
        _states[jobId] = state;
}
