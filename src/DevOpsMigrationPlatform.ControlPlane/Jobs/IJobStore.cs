// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

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

    /// <summary>
    /// Returns the state of a parent job, built from the children that carry its id
    /// (<see cref="Job.ParentJobId"/>): Failed if any child failed, Completed when every
    /// child completed, Queued while no child has started, otherwise Running.
    /// Returns <c>null</c> when no child carries the id.
    /// </summary>
    string? GetParentState(Guid parentJobId);

    /// <summary>
    /// Records that an agent advertising <paramref name="capabilities"/> asked for work now.
    /// Lease polls are the Control Plane's only sign of which agents are connected.
    /// </summary>
    void RecordAgentPoll(IReadOnlyList<ConnectorType> capabilities);

    /// <summary>
    /// Records that the agent holding <paramref name="jobId"/>'s lease is alive (it sent worker
    /// events). An agent running a job does not poll, so its lease counts as a connected agent
    /// only while this activity is recent.
    /// </summary>
    void RecordLeaseActivity(Guid jobId);

    /// <summary>
    /// Fails every job that has been leasable for at least <paramref name="timeout"/> while no
    /// agent able to serve its connectors asked for work within that time (#169, ADR-0035).
    /// Returns each failed job with an actionable reason. Held jobs are never failed here.
    /// </summary>
    IReadOnlyList<(Guid JobId, string Reason)> FailUnservableJobs(TimeSpan timeout);

    /// <summary>
    /// Fails every leased, unfinished job whose agent sent no heartbeat or worker event within
    /// <paramref name="timeout"/>: the agent has stopped, so the job would otherwise stay leased
    /// for ever (#171). Its dependents fail with it; a re-run resumes the work.
    /// </summary>
    IReadOnlyList<(Guid JobId, string Reason)> FailSilentLeases(TimeSpan timeout);

    /// <summary>
    /// Raised when the Control Plane itself fails a job, which no agent will report: its
    /// dependency failed, no connected agent can serve it, or its agent went silent mid-job.
    /// Carries the job id and an actionable reason.
    /// </summary>
    event Action<Guid, string>? JobFailedWithoutRunning;
}
