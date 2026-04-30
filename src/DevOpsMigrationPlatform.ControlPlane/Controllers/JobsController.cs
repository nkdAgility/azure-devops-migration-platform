using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
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
    /// Returns all visible jobs as a list of <see cref="JobSummary"/>.
    /// <c>GET /jobs</c>
    /// Returns 200 with JSON array (empty array when no jobs exist).
    /// </summary>
    [HttpGet("/jobs")]
    [ProducesResponseType(typeof(JobSummary[]), 200)]
    public IActionResult GetAllJobs()
    {
        var records = _jobStore.GetAllRecords();
        var summaries = records.Select(r => new JobSummary(
            Guid.Parse(r.Job.JobId),
            r.Job.Kind.ToString(),
            r.State,
            r.SubmittedByUpn,
            r.SubmittedAt
        )).ToArray();

        return Ok(summaries);
    }

    /// <summary>
    /// CLI submits a <see cref="Job"/>.
    /// <c>POST /jobs</c>
    /// Returns 201 with the assigned jobId.
    /// </summary>
    [HttpPost("/jobs")]
    [ProducesResponseType(typeof(SubmitJobResponse), 201)]
    [ProducesResponseType(400)]
    public IActionResult SubmitJob([FromBody] Job job)
    {
        if (string.IsNullOrWhiteSpace(job.JobId))
            return BadRequest("jobId is required.");

        var jobId = _jobStore.Enqueue(job);
        _logger.LogInformation("Job {JobId} accepted ({JobKind})", jobId, job.Kind);

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
    [ProducesResponseType(typeof(Job), 200)]
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
