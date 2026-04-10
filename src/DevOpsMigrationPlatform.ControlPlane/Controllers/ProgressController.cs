using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

[ApiController]
public sealed class ProgressController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly JobProgressStore _store;
    private readonly IJobStore _jobStore;
    private readonly ILeaseJobResolver _resolver;
    private readonly ILogger<ProgressController> _logger;

    public ProgressController(
        JobProgressStore store,
        IJobStore jobStore,
        ILeaseJobResolver resolver,
        ILogger<ProgressController> logger)
    {
        _store = store;
        _jobStore = jobStore;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Agent pushes a ProgressEvent for an active lease.
    /// <c>POST /agents/lease/{leaseId}/progress</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/progress")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult PostProgress(string leaseId, [FromBody] ProgressEvent evt)
    {
        var jobId = _resolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _store.Append(jobId.Value, evt);
        // First ProgressEvent transitions job from Leased → Running
        _jobStore.SetState(jobId.Value, "Running");
        return NoContent();
    }

    /// <summary>
    /// Agent signals that a job has reached a terminal state (Completed or Failed).
    /// Completes all active SSE subscriber channels so <c>migrate logs --follow</c>
    /// exits cleanly.
    /// <c>POST /agents/lease/{leaseId}/complete</c>
    /// <c>POST /agents/lease/{leaseId}/fail</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/complete")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult CompleteJob(string leaseId)
    {
        var jobId = _resolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _store.CompleteJob(jobId.Value, failed: false);
        _jobStore.SetState(jobId.Value, "Completed");
        return NoContent();
    }

    [HttpPost("/agents/lease/{leaseId}/fail")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult FailJob(string leaseId)
    {
        var jobId = _resolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _store.CompleteJob(jobId.Value, failed: true);
        _jobStore.SetState(jobId.Value, "Failed");
        return NoContent();
    }

    /// <summary>
    /// Returns a snapshot of stored ProgressEvents, or streams them via SSE when
    /// <c>follow=true</c>.
    /// <c>GET /jobs/{jobId}/progress</c>
    /// </summary>
    [HttpGet("/jobs/{jobId}/progress")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    public async Task GetProgress(Guid jobId, [FromQuery] bool follow = false, CancellationToken ct = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!follow)
        {
            var snapshot = _store.GetSnapshot(jobId);
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            await HttpContext.Response.WriteAsync(
                JsonSerializer.Serialize(snapshot, _jsonOptions), ct);
            return;
        }

        HttpContext.Response.ContentType = "text/event-stream";
        HttpContext.Response.Headers["Cache-Control"] = "no-cache";
        HttpContext.Response.Headers["X-Accel-Buffering"] = "no";

        var (reader, writer) = _store.Subscribe(jobId);
        try
        {
            // Replay any events that arrived before this SSE connection opened.
            foreach (var pastEvt in _store.GetSnapshot(jobId))
            {
                var pastJson = JsonSerializer.Serialize(pastEvt, _jsonOptions);
                await HttpContext.Response.WriteAsync($"data: {pastJson}\n\n", ct);
            }
            await HttpContext.Response.Body.FlushAsync(ct);

            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();

            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, _jsonOptions);
                await HttpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                await HttpContext.Response.Body.FlushAsync(ct);

                if (heartbeatTask.IsCompleted)
                {
                    await HttpContext.Response.WriteAsync(":\n\n", ct);
                    await HttpContext.Response.Body.FlushAsync(ct);
                    heartbeatTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();
                }
            }

            await HttpContext.Response.WriteAsync(
                _store.WasFailed(jobId)
                    ? "event: job-failed\ndata: {}\n\n"
                    : "event: job-ended\ndata: {}\n\n", ct);
            await HttpContext.Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal SSE teardown, not an error.
        }
        finally
        {
            _store.Unsubscribe(jobId, writer);
        }
    }
}
