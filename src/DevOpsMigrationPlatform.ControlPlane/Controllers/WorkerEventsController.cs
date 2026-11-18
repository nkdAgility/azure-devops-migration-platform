// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Receives batched worker events from agents via
/// <c>POST /workers/{workerId}/events</c> and dispatches to the appropriate stores.
/// This is the sole agent→CP data channel; the only other agent-originated calls
/// are lease acquisition and the lease heartbeat.
/// </summary>
[ApiController]
public sealed class WorkerEventsController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILeaseJobResolver _resolver;
    private readonly JobProgressStore _progressStore;
    private readonly DiagnosticLogStore _diagnosticStore;
    private readonly JobMetricsStore _metricsStore;
    private readonly JobSnapshotStore _snapshotStore;
    private readonly InMemoryJobTaskStore _taskStore;
    private readonly IJobStore _jobStore;
    private readonly ILogger<WorkerEventsController> _logger;

    public WorkerEventsController(
        ILeaseJobResolver resolver,
        JobProgressStore progressStore,
        DiagnosticLogStore diagnosticStore,
        JobMetricsStore metricsStore,
        JobSnapshotStore snapshotStore,
        InMemoryJobTaskStore taskStore,
        IJobStore jobStore,
        ILogger<WorkerEventsController> logger)
    {
        _resolver = resolver;
        _progressStore = progressStore;
        _diagnosticStore = diagnosticStore;
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _taskStore = taskStore;
        _jobStore = jobStore;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a batch of <see cref="WorkerEvent"/> records from a running agent.
    /// Events are dispatched by kind to the appropriate in-memory stores.
    /// Returns a <see cref="WorkerEventAck"/> with the last accepted sequence number.
    /// </summary>
    [HttpPost("/workers/{workerId}/events")]
    [ProducesResponseType(typeof(WorkerEventAck), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public IActionResult PostEvents(string workerId, [FromBody] WorkerEventBatch batch)
    {
        if (!string.Equals(workerId, batch.WorkerId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Worker event batch WorkerId mismatch: route '{RouteWorkerId}' vs body '{BodyWorkerId}'.",
                workerId, batch.WorkerId);
            return BadRequest("Route workerId does not match batch WorkerId.");
        }

        var jobId = _resolver.ResolveJobId(batch.LeaseId);
        if (jobId is null)
        {
            _logger.LogWarning(
                "Worker {WorkerId} posted events for unrecognised lease {LeaseId}.",
                workerId, batch.LeaseId);
            return NotFound($"Lease '{batch.LeaseId}' is not recognised.");
        }

        // Any worker event shows the agent holding this lease is alive (ADR-0035 fail-fast).
        _jobStore.RecordLeaseActivity(jobId.Value);

        long lastAccepted = 0;

        foreach (var evt in batch.Events)
        {
            try
            {
                DispatchEvent(jobId.Value, evt);
                lastAccepted = evt.Seq;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to dispatch worker event {Seq} (kind={Kind}) for job {JobId}.",
                    evt.Seq, evt.Kind, jobId);
            }
        }

        return Ok(new WorkerEventAck(lastAccepted));
    }

    private void DispatchEvent(Guid jobId, WorkerEvent evt)
    {
        switch (evt.Kind)
        {
            case WorkerEventKind.Heartbeat:
                // Liveness only — no payload to store.
                break;

            case WorkerEventKind.Progress:
                var progress = Deserialize<ProgressEvent>(evt.PayloadJson, evt.Seq);
                if (progress is not null)
                {
                    // Assign the monotonic sequence from the WorkerEvent so the SSE
                    // controller can track replay boundaries and clients can skip-replay.
                    progress = progress with { EventSequence = evt.Seq };
                    // The writes run atomically with terminal transitions, so a late event from an
                    // abandoned lease never reaches a finished job's stream (PR #222 review).
                    var written = _jobStore.RunIfActive(jobId, () =>
                    {
                        _progressStore.Append(jobId, progress);
                        if (progress.Metrics is not null)
                            _metricsStore.Store(jobId, progress.Metrics);
                        if (progress.TaskId is not null && progress.TaskStatus is not null)
                            _taskStore.UpdateTask(jobId, progress.TaskId, progress.TaskStatus.Value,
                                progress.CompletedCount, progress.KnownTotal, progress.Timestamp);
                    });
                    if (!written)
                    {
                        _logger.LogDebug("Dropped late progress event {Seq} for finished job {JobId}.", evt.Seq, jobId);
                        break;
                    }
                    _jobStore.TrySetState(jobId, "Running");
                }
                break;

            case WorkerEventKind.Diagnostic:
                var records = Deserialize<DiagnosticLogRecord[]>(evt.PayloadJson, evt.Seq);
                if (records is not null && !_jobStore.RunIfActive(jobId, () => _diagnosticStore.Add(jobId, records)))
                    _logger.LogDebug("Dropped late diagnostic event {Seq} for finished job {JobId}.", evt.Seq, jobId);
                break;

            case WorkerEventKind.Metrics:
                // Agents flush their final metrics before the terminal, so a finished job's
                // later Metrics, Snapshot and Tasks events are dropped too (PR #222 review).
                var metrics = Deserialize<JobMetrics>(evt.PayloadJson, evt.Seq);
                if (metrics is not null && !_jobStore.RunIfActive(jobId, () => _metricsStore.Store(jobId, metrics)))
                    _logger.LogDebug("Dropped late metrics event {Seq} for finished job {JobId}.", evt.Seq, jobId);
                break;

            case WorkerEventKind.Snapshot:
                var snapshot = Deserialize<JobSnapshot>(evt.PayloadJson, evt.Seq);
                if (snapshot is not null && !_jobStore.RunIfActive(jobId, () => _snapshotStore.Store(jobId, snapshot)))
                    _logger.LogDebug("Dropped late snapshot event {Seq} for finished job {JobId}.", evt.Seq, jobId);
                break;

            case WorkerEventKind.Tasks:
                var tasks = Deserialize<JobTaskList>(evt.PayloadJson, evt.Seq);
                if (tasks is not null && !_jobStore.RunIfActive(jobId, () => _taskStore.Store(jobId, tasks)))
                    _logger.LogDebug("Dropped late tasks event {Seq} for finished job {JobId}.", evt.Seq, jobId);
                break;

            case WorkerEventKind.Terminal:
                var failed = Deserialize<TerminalPayload>(evt.PayloadJson, evt.Seq);
                var isFailed = failed?.Failed ?? false;
                // Only the event that ends the job completes its streams: a late terminal from an
                // abandoned lease must not replace a recorded failure with success (PR #222 review).
                if (!_jobStore.TrySetState(jobId, isFailed ? "Failed" : "Completed"))
                {
                    _logger.LogWarning("Dropped late terminal event {Seq} for job {JobId}, which has already finished.", evt.Seq, jobId);
                    break;
                }
                _progressStore.CompleteJob(jobId, isFailed);
                _diagnosticStore.CompleteJob(jobId, isFailed);
                break;

            default:
                _logger.LogWarning("Unrecognised WorkerEventKind {Kind} for job {JobId}.", evt.Kind, jobId);
                break;
        }
    }

    private T? Deserialize<T>(string? json, long seq) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("Worker event seq={Seq} has null/empty payload for type {Type}.", seq, typeof(T).Name);
            return null;
        }
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private sealed record TerminalPayload(bool Failed);
}
