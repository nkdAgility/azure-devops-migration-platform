using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Handles telemetry snapshot endpoints for running jobs.
/// </summary>
[ApiController]
public sealed class TelemetryController : ControllerBase
{
    private readonly JobTelemetryStore _telemetryStore;
    private readonly ILeaseJobResolver _leaseResolver;

    public TelemetryController(
        JobTelemetryStore telemetryStore,
        ILeaseJobResolver leaseResolver)
    {
        _telemetryStore = telemetryStore;
        _leaseResolver  = leaseResolver;
    }

    /// <summary>
    /// Migration Agent pushes a MetricSnapshot for an active lease.
    /// <c>POST /agents/lease/{leaseId}/telemetry</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/telemetry")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public IActionResult PushTelemetry(string leaseId, [FromBody] MetricSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
            return BadRequest("leaseId must not be empty.");

        var jobId = _leaseResolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _telemetryStore.Store(jobId.Value, snapshot);
        return NoContent();
    }

    /// <summary>
    /// Returns the latest MetricSnapshot for a job.
    /// <c>GET /jobs/{jobId}/telemetry</c>
    /// Returns 200+body when a snapshot exists, 204 when none yet, 400 for bad id.
    /// </summary>
    [HttpGet("/jobs/{jobId}/telemetry")]
    [ProducesResponseType(200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public IActionResult GetTelemetry(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return BadRequest("jobId must be a valid GUID.");

        var snapshot = _telemetryStore.GetLatest(id);
        return snapshot is null ? NoContent() : Ok(snapshot);
    }
}
