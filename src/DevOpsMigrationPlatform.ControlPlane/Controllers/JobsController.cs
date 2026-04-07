using System;
using System.Text.Json;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

[ApiController]
public sealed class JobsController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IJobStore _jobStore;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IJobStore jobStore, ILogger<JobsController> logger)
    {
        _jobStore = jobStore;
        _logger = logger;
    }

    /// <summary>
    /// CLI submits a <see cref="MigrationJob"/>.
    /// <c>POST /jobs</c>
    /// Returns 201 with the assigned jobId.
    /// </summary>
    [HttpPost("/jobs")]
    [ProducesResponseType(typeof(SubmitJobResponse), 201)]
    [ProducesResponseType(400)]
    public IActionResult SubmitJob([FromBody] MigrationJob job)
    {
        if (string.IsNullOrWhiteSpace(job.JobId))
            return BadRequest("jobId is required.");
        if (string.IsNullOrWhiteSpace(job.Mode))
            return BadRequest("mode is required.");

        var jobId = _jobStore.Enqueue(job);
        _logger.LogInformation("Job {JobId} accepted (mode={Mode})", jobId, job.Mode);

        return CreatedAtAction(
            nameof(GetJob),
            new { jobId = jobId.ToString() },
            new SubmitJobResponse(jobId));
    }

    /// <summary>
    /// Returns the job with the given id.
    /// <c>GET /jobs/{jobId}</c>
    /// </summary>
    [HttpGet("/jobs/{jobId}")]
    [ProducesResponseType(typeof(MigrationJob), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetJob(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return BadRequest("jobId must be a valid GUID.");

        var job = _jobStore.Get(id);
        if (job is null)
            return NotFound();

        return Ok(job);
    }
}

/// <summary>Response body for a successful job submission.</summary>
public sealed record SubmitJobResponse(Guid JobId);
