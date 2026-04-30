using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Models;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Persistence contract for <see cref="Job"/> instances submitted to the control plane.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Stores a submitted job and enqueues it for agent pickup.
    /// Returns the job id.
    /// </summary>
    Guid Enqueue(Job job);

    /// <summary>
    /// Dequeues one pending job, waiting up to <paramref name="timeout"/>.
    /// Returns <c>null</c> if no job became available within the timeout.
    /// </summary>
    Task<Job?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Dequeues one pending job whose <see cref="Job.Connectors"/> are all satisfied by
    /// the agent's advertised <paramref name="capabilities"/>. Non-matching jobs are re-enqueued.
    /// Returns <c>null</c> if no matching job is found within <paramref name="timeout"/>.
    /// </summary>
    Task<Job?> DequeueAsync(TimeSpan timeout, IReadOnlyList<ConnectorType> capabilities, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a snapshot of all submitted jobs.
    /// </summary>
    IReadOnlyList<Job> GetAll();

    /// <summary>
    /// Returns the job with the given id, or <c>null</c> if not found.
    /// </summary>
    Job? Get(Guid jobId);

    /// <summary>
    /// Returns all submitted jobs with their runtime state, submission time, and submitter identity.
    /// </summary>
    IReadOnlyList<JobRecord> GetAllRecords();

    /// <summary>
    /// Updates the tracked state for a job (e.g. Queued → Leased → Running → Completed/Failed).
    /// </summary>
    void SetState(Guid jobId, string state);
}
