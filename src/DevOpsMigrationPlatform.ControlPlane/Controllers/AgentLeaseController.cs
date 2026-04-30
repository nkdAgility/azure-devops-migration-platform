using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.ControlPlane.Models;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

[ApiController]
public sealed class AgentLeaseController : ControllerBase
{
    private static readonly TimeSpan LeasePollTimeout = TimeSpan.FromSeconds(30);

    private readonly IJobStore _jobStore;
    private readonly ILeaseJobResolver _resolver;
    private readonly ILogger<AgentLeaseController> _logger;

    public AgentLeaseController(
        IJobStore jobStore,
        ILeaseJobResolver resolver,
        ILogger<AgentLeaseController> logger)
    {
        _jobStore = jobStore;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Agent polls for a pending job. Long-polls up to 30 s then returns 204 if idle.
    /// <c>GET /agents/lease</c>
    /// Returns 200 + <see cref="AgentLeaseResponse"/> when a job is available.
    /// Returns 204 (no content) when no job is pending within the timeout.
    /// </summary>
    [HttpGet("/agents/lease")]
    [ProducesResponseType(typeof(AgentLeaseResponse), 200)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> AcquireLease(
        [FromQuery] string? capabilities,
        CancellationToken cancellationToken)
    {
        Job? job;
        try
        {
            if (!string.IsNullOrWhiteSpace(capabilities))
            {
                var caps = capabilities
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .Select(c => Enum.TryParse<ConnectorType>(c, ignoreCase: true, out var ct) ? ct : (ConnectorType?)null)
                    .Where(c => c.HasValue)
                    .Select(c => c!.Value)
                    .ToList();

                job = await _jobStore
                    .DequeueAsync(LeasePollTimeout, caps, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                job = await _jobStore
                    .DequeueAsync(LeasePollTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return NoContent();
        }

        if (job is null)
            return NoContent();

        var leaseId = Guid.NewGuid().ToString("N");
        var jobId = Guid.Parse(job.JobId);
        _resolver.RegisterLease(leaseId, jobId);
        _jobStore.SetState(jobId, "Leased");

        _logger.LogInformation(
            "Lease {LeaseId} granted to agent for job {JobId}", leaseId, jobId);

        return Ok(new AgentLeaseResponse(leaseId, job));
    }
}
