// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Handles telemetry snapshot endpoints for running jobs.
/// </summary>
[ApiController]
public sealed class TelemetryController : ControllerBase
{
    private readonly JobMetricsStore _telemetryStore;
    private readonly JobSnapshotStore _snapshotStore;
    private readonly JobProgressStore _progressStore;
    private readonly InMemoryJobTaskStore _taskStore;
    private readonly ILeaseJobResolver _leaseResolver;

    public TelemetryController(
        JobMetricsStore telemetryStore,
        JobSnapshotStore snapshotStore,
        JobProgressStore progressStore,
        InMemoryJobTaskStore taskStore,
        ILeaseJobResolver leaseResolver)
    {
        _telemetryStore = telemetryStore;
        _snapshotStore = snapshotStore;
        _progressStore = progressStore;
        _taskStore = taskStore;
        _leaseResolver = leaseResolver;
    }

    /// <summary>
    /// Migration Agent pushes <see cref="JobMetrics"/> for an active lease.
    /// <c>POST /agents/lease/{leaseId}/metrics</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/metrics")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public IActionResult PushTelemetry(string leaseId, [FromBody] JobMetrics metrics)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
            return BadRequest("leaseId must not be empty.");

        var jobId = _leaseResolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _telemetryStore.Store(jobId.Value, metrics);
        return NoContent();
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
    /// Migration Agent pushes <see cref="JobSnapshot"/> for an active lease.
    /// <c>POST /agents/lease/{leaseId}/snapshot</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/snapshot")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public IActionResult PushSnapshot(string leaseId, [FromBody] JobSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
            return BadRequest("leaseId must not be empty.");

        var jobId = _leaseResolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _snapshotStore.Store(jobId.Value, snapshot);
        return NoContent();
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
    /// Migration Agent pushes its execution plan (task list) at job start.
    /// <c>POST /agents/lease/{leaseId}/tasks</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/tasks")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public IActionResult PushTasks(string leaseId, [FromBody] JobTaskList taskList)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
            return BadRequest("leaseId must not be empty.");

        var jobId = _leaseResolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _taskStore.Store(jobId.Value, taskList);
        return NoContent();
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
