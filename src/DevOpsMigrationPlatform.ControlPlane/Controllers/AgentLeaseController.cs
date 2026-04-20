using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Models;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

[ApiController]
public sealed class AgentLeaseController : ControllerBase
{
    private static readonly TimeSpan LeasePollTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// In-memory registry of active agent instances.
    /// Key: agentInstanceId (GUID string). Value: UTC timestamp of last seen poll.
    /// Populated from the <c>agentInstanceId</c> query parameter on <c>GET /agents/lease</c>.
    /// Used by <c>GET /agents/{agentInstanceId}/status</c> for stale-lock detection.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _activeAgents = new(StringComparer.OrdinalIgnoreCase);

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
    /// <c>GET /agents/lease?agentInstanceId={guid}</c>
    /// Returns 200 + <see cref="AgentLeaseResponse"/> when a job is available.
    /// Returns 204 (no content) when no job is pending within the timeout.
    /// The optional <paramref name="agentInstanceId"/> query parameter is recorded in the
    /// active-agent registry for stale-lock liveness checks.
    /// </summary>
    [HttpGet("/agents/lease")]
    [ProducesResponseType(typeof(AgentLeaseResponse), 200)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> AcquireLease(
        [FromQuery] string? agentInstanceId,
        CancellationToken cancellationToken)
    {
        // Record the agent as active if it identified itself.
        if (!string.IsNullOrEmpty(agentInstanceId))
            _activeAgents[agentInstanceId] = DateTimeOffset.UtcNow;

        Job? job;
        try
        {
            job = await _jobStore
                .DequeueAsync(LeasePollTimeout, cancellationToken)
                .ConfigureAwait(false);
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
            "Lease {LeaseId} granted to agent {AgentInstanceId} for job {JobId}",
            leaseId, agentInstanceId ?? "(unknown)", jobId);

        return Ok(new AgentLeaseResponse(leaseId, job));
    }

    /// <summary>
    /// Returns the liveness status of an agent instance.
    /// <c>GET /agents/{agentInstanceId}/status</c>
    /// Returns 200 + <c>{ "status": "Active" }</c> when the agent has polled recently.
    /// Returns 404 when the agent is unknown or has not polled within 5 minutes.
    /// Consumed by <c>PackageLockFileService</c> in the Migration Agent to detect stale locks.
    /// </summary>
    [HttpGet("/agents/{agentInstanceId}/status")]
    [ProducesResponseType(typeof(AgentStatusResponse), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetAgentStatus(string agentInstanceId)
    {
        if (!_activeAgents.TryGetValue(agentInstanceId, out var lastSeen))
            return NotFound();

        // Consider stale if no poll in the last 5 minutes (lease poll interval is 5 s + 30 s long-poll).
        var staleness = DateTimeOffset.UtcNow - lastSeen;
        if (staleness > TimeSpan.FromMinutes(5))
        {
            _activeAgents.TryRemove(agentInstanceId, out _);
            return NotFound();
        }

        return Ok(new AgentStatusResponse("Active"));
    }
}

