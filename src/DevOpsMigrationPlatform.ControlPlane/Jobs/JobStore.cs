// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

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

    // Jobs waiting for the job they depend on (ADR-0035): dependency id -> dependent ids.
    // Held jobs are not in _pending, so no agent can lease them until they are released.
    private readonly Dictionary<Guid, List<Guid>> _held = new();
    private readonly object _holdLock = new();

    // When each queued job became leasable (entered _pending), for the unservable-job check.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _waitingSince = new();

    // Last lease poll seen per advertised connector set (key: sorted connector names; "*" = any).
    private readonly ConcurrentDictionary<string, (ConnectorType[] Connectors, DateTimeOffset LastSeen)> _agentPolls = new();

    // Connectors of the agent each job was leased to. An agent runs its leased job to the end
    // before it polls again, so an unfinished lease also shows that agent is connected.
    private readonly ConcurrentDictionary<Guid, (ConnectorType[] Connectors, DateTimeOffset LastSeen)> _leasedTo = new();
    private const string AnyConnectorKey = "*";

    private readonly IJobLifecycleMetrics? _metrics;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time;

    /// <inheritdoc />
    public event Action<Guid, string>? JobFailedWithoutRunning;

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.ControlPlane);

    public JobStore(IJobLifecycleMetrics? metrics = null, ILogger<JobStore>? logger = null, TimeProvider? timeProvider = null)
    {
        _metrics = metrics;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Guid Enqueue(Job job)
    {
        using var activity = ActivitySource.StartActivity("job.enqueue", ActivityKind.Internal);
        activity?.SetTag("job.id", job.JobId);
        activity?.SetTag("job.type", GetJobType(job));

        var jobId = Guid.Parse(job.JobId);
        _all[jobId] = job;
        lock (_claimLock)
        {
            _states[jobId] = "Queued";
            _claimed.Remove(jobId);
        }
        _submittedAt[jobId] = DateTimeOffset.UtcNow;
        EnqueueOrHold(jobId, job);

        _metrics?.JobSubmitted(new TagList
        {
            { "job.id", job.JobId },
            { "job.type", GetJobType(job) }
        });

        _logger?.LogInformation("[ControlPlane] Job {JobId} ({JobType}) enqueued.", job.JobId, GetJobType(job));

        return jobId;
    }

    /// <inheritdoc />
    public async Task<Job?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        RecordPoll(AnyConnectorKey, []);
        if (!await _pendingSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            return null;

        if (_pending.TryDequeue(out var jobId) && _all.TryGetValue(jobId, out var job) && TryClaim(jobId))
        {
            _waitingSince.TryRemove(jobId, out _);
            using var activity = ActivitySource.StartActivity("job.dequeue", ActivityKind.Internal);
            activity?.SetTag("job.id", job.JobId);
            activity?.SetTag("job.type", GetJobType(job));

            _metrics?.JobDequeued(new TagList
            {
                { "job.id", job.JobId },
                { "job.type", GetJobType(job) }
            });
            _logger?.LogDebug("[ControlPlane] Job {JobId} ({JobType}) dequeued for processing.", job.JobId, GetJobType(job));
            return job;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Job?> DequeueAsync(TimeSpan timeout, IReadOnlyList<ConnectorType> capabilities, CancellationToken cancellationToken)
    {
        var agentConnectors = new HashSet<ConnectorType>(capabilities);
        RecordAgentPoll(capabilities);

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            if (!await _pendingSignal.WaitAsync(remaining, cancellationToken).ConfigureAwait(false))
                return null;

            if (_pending.TryDequeue(out var jobId) && _all.TryGetValue(jobId, out var job))
            {
                // A job failed while queued (e.g. unservable) is dropped, never leased.
                if (!IsLeasable(jobId))
                    continue;

                // A job matches if every connector it demands is advertised by this agent.
                // A job with no connectors (e.g. Simulated) matches any agent.
                var isMatch = job.Connectors.Length == 0 || job.Connectors.All(c => agentConnectors.Contains(c));
                if (isMatch)
                {
                    // Lost to an unservable-job failure between the checks above and here.
                    if (!TryClaim(jobId))
                        continue;
                    _waitingSince.TryRemove(jobId, out _);
                    _leasedTo[jobId] = (agentConnectors.OrderBy(c => c).ToArray(), _time.GetUtcNow());
                    using var activity = ActivitySource.StartActivity("job.dequeue", ActivityKind.Internal);
                    activity?.SetTag("job.id", job.JobId);
                    activity?.SetTag("job.type", GetJobType(job));

                    _metrics?.JobDequeued(new TagList
                    {
                        { "job.id", job.JobId },
                        { "job.type", GetJobType(job) }
                    });
                    _logger?.LogDebug("[ControlPlane] Job {JobId} ({JobType}) dequeued for agent with connectors [{Connectors}].",
                        job.JobId, GetJobType(job), string.Join(", ", capabilities));
                    return job;
                }

                // Re-enqueue non-matching job.
                _pending.Enqueue(jobId);
                _pendingSignal.Release();
                _logger?.LogDebug(
                    "[ControlPlane] Job {JobId} connectors [{JobConnectors}] do not match agent connectors [{AgentConnectors}] — re-queued.",
                    job.JobId,
                    string.Join(", ", job.Connectors),
                    string.Join(", ", capabilities));
            }
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
    public void SetState(Guid jobId, string state) => TrySetState(jobId, state);

    /// <inheritdoc />
    public bool RunIfActive(Guid jobId, Action write)
    {
        // Under the lock every transition takes, so a terminal transition either happens before
        // (the write is refused) or waits until the write is done (PR #222 review).
        lock (_holdLock)
        {
            if (_states.TryGetValue(jobId, out var state) && state is "Completed" or "Failed")
                return false;
            write();
            return true;
        }
    }

    /// <inheritdoc />
    public bool TrySetState(Guid jobId, string state)
    {
        using var activity = ActivitySource.StartActivity("job.setState", ActivityKind.Internal);
        activity?.SetTag("job.id", jobId.ToString());
        activity?.SetTag("job.state", state);

        string? previousState;
        List<Guid>? released = null;
        lock (_holdLock)
        {
            previousState = _states.TryGetValue(jobId, out var prev) ? prev : null;
            // A finished job keeps its final state (PR #222 review): a silent-lease failure must
            // not overwrite a completion, and a late event from an abandoned lease must not
            // revive a failed job. Enqueue resets a resubmitted job directly.
            if (previousState is "Completed" or "Failed")
            {
                if (previousState != state)
                    _logger?.LogWarning("[ControlPlane] Job {JobId} is {FinalState}; ignoring a late change to {LateState}.", jobId, previousState, state);
                return false;
            }
            _states[jobId] = state;
            if (state is "Completed" or "Failed" && _held.Remove(jobId, out var dependents))
                released = dependents;
        }

        _logger?.LogDebug("[ControlPlane] Job {JobId} state: {PreviousState} → {NewState}.", jobId, previousState ?? "(none)", state);

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

            double? durationMs = null;
            if (_startedAt.TryRemove(jobId, out var started))
            {
                durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                _metrics?.RecordJobDuration(durationMs.Value, tags);
            }

            if (state == "Completed")
            {
                _metrics?.JobCompleted(tags);
                if (durationMs.HasValue)
                    _logger?.LogInformation("[ControlPlane] Job {JobId} completed in {DurationMs:F0}ms.", jobId, durationMs.Value);
                else
                    _logger?.LogInformation("[ControlPlane] Job {JobId} completed.", jobId);
            }
            else
            {
                _metrics?.JobFailed(tags);
                _logger?.LogWarning("[ControlPlane] Job {JobId} failed.", jobId);
            }
        }

        if (released is not null)
            ReleaseDependents(jobId, state, released);
        return true;
    }

    /// <inheritdoc />
    public string? GetParentState(Guid parentJobId)
    {
        var parentId = parentJobId.ToString();
        var childStates = _all
            .Where(kv => string.Equals(kv.Value.ParentJobId, parentId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => _states.TryGetValue(kv.Key, out var s) ? s : "Queued")
            .ToList();

        if (childStates.Count == 0) return null;
        if (childStates.Contains("Failed")) return "Failed";
        if (childStates.All(s => s == "Completed")) return "Completed";
        if (childStates.All(s => s == "Queued")) return "Queued";
        return "Running";
    }

    private void EnqueueOrHold(Guid jobId, Job job)
    {
        if (job.DependsOnJobId is { } dependsOn && Guid.TryParse(dependsOn, out var dependencyId))
        {
            string? dependencyState;
            lock (_holdLock)
            {
                dependencyState = _states.TryGetValue(dependencyId, out var s) ? s : null;
                if (dependencyState is not ("Completed" or "Failed"))
                {
                    if (!_held.TryGetValue(dependencyId, out var dependents))
                        _held[dependencyId] = dependents = new List<Guid>();
                    dependents.Add(jobId);
                    _logger?.LogInformation("[ControlPlane] Job {JobId} held until job {DependencyId} completes.", jobId, dependencyId);
                    return;
                }
            }

            if (dependencyState == "Failed")
            {
                FailDependent(jobId, dependencyId);
                return;
            }
        }

        MakeLeasable(jobId);
    }

    private void MakeLeasable(Guid jobId)
    {
        _waitingSince[jobId] = _time.GetUtcNow();
        _pending.Enqueue(jobId);
        _pendingSignal.Release();
    }

    private bool IsLeasable(Guid jobId) =>
        _states.TryGetValue(jobId, out var state) && state == "Queued";

    // A queued job is either leased to an agent or failed as unservable, never both: each side
    // must claim it first, and only the first claim succeeds.
    private readonly object _claimLock = new();
    private readonly HashSet<Guid> _claimed = new();

    private bool TryClaim(Guid jobId)
    {
        lock (_claimLock)
            return IsLeasable(jobId) && _claimed.Add(jobId);
    }

    /// <inheritdoc />
    public void RecordAgentPoll(IReadOnlyList<ConnectorType> capabilities)
    {
        var connectors = capabilities.Distinct().OrderBy(c => c).ToArray();
        // "*" is only for the unrestricted dequeue; an empty advertised set serves only
        // connector-less jobs, so it is recorded under its own (empty) key.
        RecordPoll(string.Join(",", connectors), connectors);
    }

    private void RecordPoll(string key, ConnectorType[] connectors)
    {
        // Under the claim lock, so a poll lands either before an unservable check (and keeps
        // the job queued) or after the job was already claimed, never in between.
        lock (_claimLock)
            _agentPolls[key] = (connectors, _time.GetUtcNow());
    }

    /// <inheritdoc />
    public void RecordLeaseActivity(Guid jobId)
    {
        if (_leasedTo.TryGetValue(jobId, out var lease))
            _leasedTo[jobId] = (lease.Connectors, _time.GetUtcNow());
    }

    /// <summary>
    /// Connector sets of the agents seen within <paramref name="timeout"/>: recent lease polls,
    /// plus agents holding an unfinished lease that sent worker events recently. Null means an
    /// unrestricted agent. Call under <see cref="_claimLock"/>.
    /// </summary>
    private List<ConnectorType[]?> ConnectedAgents(DateTimeOffset now, TimeSpan timeout)
    {
        var agents = _agentPolls
            .Where(kv => now - kv.Value.LastSeen <= timeout)
            .Select(kv => kv.Key == AnyConnectorKey ? null : kv.Value.Connectors)
            .ToList();
        foreach (var (leasedJobId, lease) in _leasedTo)
        {
            var leaseState = _states.TryGetValue(leasedJobId, out var s) ? s : null;
            if (leaseState is "Completed" or "Failed")
                _leasedTo.TryRemove(leasedJobId, out _);
            else if (now - lease.LastSeen <= timeout)
                agents.Add(lease.Connectors);
        }
        return agents;
    }

    /// <inheritdoc />
    public IReadOnlyList<(Guid JobId, string Reason)> FailUnservableJobs(TimeSpan timeout)
    {
        var failed = new List<(Guid JobId, string Reason)>();
        foreach (var (jobId, since) in _waitingSince)
        {
            if (_time.GetUtcNow() - since < timeout || !_all.TryGetValue(jobId, out var job))
                continue;

            // The connected-agent check and the failure claim are one step under the claim
            // lock, so an agent that polls or leases the job meanwhile always wins.
            lock (_claimLock)
            {
                var servable = ConnectedAgents(_time.GetUtcNow(), timeout).Any(agent =>
                    agent is null || job.Connectors.All(c => agent.Contains(c)));
                if (servable || !IsLeasable(jobId) || !_claimed.Add(jobId))
                    continue;
            }

            var reason = UnservableReason(job, timeout);
            _waitingSince.TryRemove(jobId, out _);
            FailWithoutRunning(jobId, reason);
            failed.Add((jobId, reason));
        }

        return failed;
    }

    /// <inheritdoc />
    public IReadOnlyList<(Guid JobId, string Reason)> FailSilentLeases(TimeSpan timeout)
    {
        var failed = new List<(Guid JobId, string Reason)>();
        var now = _time.GetUtcNow();
        foreach (var (jobId, lease) in _leasedTo)
        {
            if (now - lease.LastSeen <= timeout || !_all.TryGetValue(jobId, out var job))
                continue;

            // Remove only the lease entry seen here, so activity recorded meanwhile wins.
            if (!_leasedTo.TryRemove(new KeyValuePair<Guid, (ConnectorType[], DateTimeOffset)>(jobId, lease)))
                continue;
            // No separate state read: the transition alone decides. A job that already finished,
            // at any point before this line, refuses it, so nothing is published; its lease is
            // simply cleared. Side effects run outside any lock (PR #222 review).
            if (!TrySetState(jobId, "Failed"))
                continue;

            var agents = string.Join(" or ", job.Connectors.Select(AgentFor).Distinct());
            var reason = $"{job.Kind} job {job.JobId} failed: its agent ({(agents.Length == 0 ? "MigrationAgent" : agents)}) " +
                         $"sent no heartbeat for {timeout.TotalMinutes:0.#} minutes and has probably stopped. " +
                         "Check the agent's log, then run the migration again; completed work is skipped.";
            _logger?.LogWarning("[ControlPlane] {Reason}", reason);
            JobFailedWithoutRunning?.Invoke(jobId, reason);
            failed.Add((jobId, reason));
        }
        return failed;
    }

    private static string UnservableReason(Job job, TimeSpan timeout)
    {
        var needed = string.Join(", ", job.Connectors);
        var agents = string.Join(" and ", job.Connectors.Select(AgentFor).Distinct());
        return $"No connected agent can run {job.Kind} job {job.JobId}: it needs the [{needed}] connector, " +
               $"and no agent advertising it has asked for work in the last {timeout.TotalMinutes:0.#} minutes. " +
               $"Start {agents} against this Control Plane, then queue the job again.";
    }

    private static string AgentFor(ConnectorType connector) => connector switch
    {
        ConnectorType.TeamFoundationServer => "TfsMigrationAgent (net481, Windows only)",
        _ => "MigrationAgent"
    };

    private void ReleaseDependents(Guid dependencyId, string dependencyState, List<Guid> dependents)
    {
        foreach (var dependentId in dependents)
        {
            if (dependencyState == "Completed")
            {
                MakeLeasable(dependentId);
                _logger?.LogInformation("[ControlPlane] Job {JobId} released: job {DependencyId} completed.", dependentId, dependencyId);
            }
            else
            {
                FailDependent(dependentId, dependencyId);
            }
        }
    }

    private void FailDependent(Guid dependentId, Guid dependencyId) =>
        FailWithoutRunning(dependentId, $"Job {dependentId} was not run because job {dependencyId}, which it depends on, failed.");

    private void FailWithoutRunning(Guid jobId, string reason)
    {
        _logger?.LogWarning("[ControlPlane] {Reason}", reason);
        SetState(jobId, "Failed");
        JobFailedWithoutRunning?.Invoke(jobId, reason);
    }

    private static string GetJobType(Job job) => job.Kind.ToString();
}
