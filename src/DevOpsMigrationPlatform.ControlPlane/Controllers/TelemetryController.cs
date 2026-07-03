// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Serves telemetry reads for running jobs: latest metrics, snapshot, task list,
/// and the atomic bootstrap response. Telemetry data arrives via the unified
/// <c>POST /workers/{workerId}/events</c> channel.
/// </summary>
[ApiController]
public sealed class TelemetryController : ControllerBase
{
    private readonly JobMetricsStore _telemetryStore;
    private readonly JobSnapshotStore _snapshotStore;
    private readonly JobProgressStore _progressStore;
    private readonly InMemoryJobTaskStore _taskStore;

    public TelemetryController(
        JobMetricsStore telemetryStore,
        JobSnapshotStore snapshotStore,
        JobProgressStore progressStore,
        InMemoryJobTaskStore taskStore)
    {
        _telemetryStore = telemetryStore;
        _snapshotStore = snapshotStore;
        _progressStore = progressStore;
        _taskStore = taskStore;
    }

    /// <summary>
    /// Returns the latest <see cref="JobMetrics"/> for a job.
    /// <c>GET /jobs/{jobId}/telemetry</c>
    /// Returns 200+body when metrics exist, 204 when none yet, 400 for bad id.
    /// </summary>
    [HttpGet("/jobs/{jobId}/telemetry")]
    [ProducesResponseType(200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult GetTelemetry(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return BadRequest("jobId must be a valid GUID.");

        var metrics = _telemetryStore.GetLatest(id);
        return metrics is null ? NoContent() : Ok(metrics);
    }

    /// <summary>
    /// Returns the latest <see cref="JobSnapshot"/> for a job.
    /// <c>GET /jobs/{jobId}/snapshot</c>
    /// Returns 200+body when a snapshot exists, 204 when none yet, 400 for bad id.
    /// </summary>
    [HttpGet("/jobs/{jobId}/snapshot")]
    [ProducesResponseType(200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult GetSnapshot(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return BadRequest("jobId must be a valid GUID.");

        var snapshot = _snapshotStore.GetLatest(id);
        return snapshot is null ? NoContent() : Ok(snapshot);
    }

    /// <summary>
    /// Atomic bootstrap response for late-joining clients.
    /// <c>GET /jobs/{jobId}/bootstrap</c>
    /// Returns the latest snapshot, metrics, and last event sequence in a single response.
    /// </summary>
    [HttpGet("/jobs/{jobId}/bootstrap")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public IActionResult GetBootstrap(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return BadRequest("jobId must be a valid GUID.");

        var bootstrap = new JobBootstrap
        {
            Snapshot = _snapshotStore.GetLatest(id),
            Metrics = _telemetryStore.GetLatest(id),
            LastEventSequence = _progressStore.GetMaxEventSequence(id),
            Tasks = _taskStore.GetLatest(id)
        };

        return Ok(bootstrap);
    }

    /// <summary>
    /// Returns the current task list for a job.
    /// <c>GET /jobs/{jobId}/tasks</c>
    /// Returns 200+body when a task list exists, 204 when none yet, 400 for bad id.
    /// </summary>
    [HttpGet("/jobs/{jobId}/tasks")]
    [ProducesResponseType(200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult GetTasks(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return BadRequest("jobId must be a valid GUID.");

        var tasks = _taskStore.GetLatest(id);
        return tasks is null ? NoContent() : Ok(tasks);
    }
}
