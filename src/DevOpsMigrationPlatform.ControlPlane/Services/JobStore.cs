using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _startedAt = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly ConcurrentQueue<Guid> _pending = new();
    private readonly IJobLifecycleMetrics? _metrics;

    public JobStore(IJobLifecycleMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    /// <inheritdoc />
    public Guid Enqueue(Job job)
    {
        var jobId = Guid.Parse(job.JobId);
        _all[jobId] = job;
        _states[jobId] = "Queued";
        _submittedAt[jobId] = DateTimeOffset.UtcNow;
        _pending.Enqueue(jobId);
        _pendingSignal.Release();

        _metrics?.JobSubmitted(new TagList
        {
            { "job.id", job.JobId },
            { "job.type", GetJobType(job) }
        });

        return jobId;
    }

    /// <inheritdoc />
    public async Task<Job?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _pendingSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            return null;

        if (_pending.TryDequeue(out var jobId) && _all.TryGetValue(jobId, out var job))
        {
            _metrics?.JobDequeued(new TagList
            {
                { "job.id", job.JobId },
                { "job.type", GetJobType(job) }
            });
            return job;
        }

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
    public void SetState(Guid jobId, string state)
    {
        var previousState = _states.TryGetValue(jobId, out var prev) ? prev : null;
        _states[jobId] = state;

        // Only fire JobStarted once (first transition to Running).
        if (state == "Running" && previousState != "Running")
        {
            _startedAt.TryAdd(jobId, DateTimeOffset.UtcNow);
            if (_all.TryGetValue(jobId, out var runningJob))
            {
                _metrics?.JobStarted(new TagList
                {
                    { "job.id", runningJob.JobId },
                    { "job.type", GetJobType(runningJob) }
                });
            }
        }

        if (state is "Completed" or "Failed" && _all.TryGetValue(jobId, out var job))
        {
            var tags = new TagList
            {
                { "job.id", job.JobId },
                { "job.type", GetJobType(job) }
            };

            if (_startedAt.TryRemove(jobId, out var started))
            {
                var durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                _metrics?.RecordJobDuration(durationMs, tags);
            }

            if (state == "Completed")
                _metrics?.JobCompleted(tags);
            else
                _metrics?.JobFailed(tags);
        }
    }

    private static string GetJobType(Job job) =>
        job is MigrationJob ? "migration" : (job is DiscoveryJob ? "discovery" : "unknown");
}
