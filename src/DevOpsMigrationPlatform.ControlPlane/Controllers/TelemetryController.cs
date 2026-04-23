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
}
