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
/// Replaces the individual per-signal endpoints as the primary agent→CP channel.
/// The old per-signal endpoints remain as shims for backwards compatibility.
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
    [ProducesResponseType(404)]
    public IActionResult PostEvents(string workerId, [FromBody] WorkerEventBatch batch)
    {
        var jobId = _resolver.ResolveJobId(batch.LeaseId);
        if (jobId is null)
        {
            _logger.LogWarning(
                "Worker {WorkerId} posted events for unrecognised lease {LeaseId}.",
                workerId, batch.LeaseId);
            return NotFound($"Lease '{batch.LeaseId}' is not recognised.");
        }

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
                    _progressStore.Append(jobId, progress);
                    if (progress.Metrics is not null)
                        _metricsStore.Store(jobId, progress.Metrics);
                    if (progress.TaskId is not null && progress.TaskStatus is not null)
                        _taskStore.UpdateTask(jobId, progress.TaskId, progress.TaskStatus.Value,
                            progress.CompletedCount, progress.KnownTotal, progress.Timestamp);
                    _jobStore.SetState(jobId, "Running");
                }
                break;

            case WorkerEventKind.Diagnostic:
                var records = Deserialize<DiagnosticLogRecord[]>(evt.PayloadJson, evt.Seq);
                if (records is not null)
                    _diagnosticStore.Add(jobId, records);
                break;

            case WorkerEventKind.Metrics:
                var metrics = Deserialize<JobMetrics>(evt.PayloadJson, evt.Seq);
                if (metrics is not null)
                    _metricsStore.Store(jobId, metrics);
                break;

            case WorkerEventKind.Snapshot:
                var snapshot = Deserialize<JobSnapshot>(evt.PayloadJson, evt.Seq);
                if (snapshot is not null)
                    _snapshotStore.Store(jobId, snapshot);
                break;

            case WorkerEventKind.Tasks:
                var tasks = Deserialize<JobTaskList>(evt.PayloadJson, evt.Seq);
                if (tasks is not null)
                    _taskStore.Store(jobId, tasks);
                break;

            case WorkerEventKind.Terminal:
                var failed = Deserialize<TerminalPayload>(evt.PayloadJson, evt.Seq);
                var isFailed = failed?.Failed ?? false;
                _progressStore.CompleteJob(jobId, isFailed);
                _diagnosticStore.CompleteJob(jobId, isFailed);
                _jobStore.SetState(jobId, isFailed ? "Failed" : "Completed");
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
