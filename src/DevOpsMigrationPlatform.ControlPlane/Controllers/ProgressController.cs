using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
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
    private readonly JobMetricsStore _metricsStore;
    private readonly InMemoryJobTaskStore _taskStore;

    private readonly DiagnosticLogStore _diagnosticStore;

    public ProgressController(
        JobProgressStore store,
        DiagnosticLogStore diagnosticStore,
        JobMetricsStore metricsStore,
        InMemoryJobTaskStore taskStore,
        IJobStore jobStore,
        ILeaseJobResolver resolver,
        ILogger<ProgressController> logger)
    {
        _store = store;
        _diagnosticStore = diagnosticStore;
        _metricsStore = metricsStore;
        _taskStore = taskStore;
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

        // Forward Channel 1 metrics to the metrics store so the bootstrap endpoint
        // and telemetry polling return data immediately — without waiting for the
        // Channel 2 SnapshotMetricExporter → ControlPlaneTelemetryTimer push cycle.
        if (evt.Metrics is not null)
            _metricsStore.Store(jobId.Value, evt.Metrics);

        // Derive task-level status transitions from ProgressEvent.TaskId / TaskStatus.
        if (evt.TaskId is not null && evt.TaskStatus is not null)
        {
            _taskStore.UpdateTask(
                jobId.Value,
                evt.TaskId,
                evt.TaskStatus.Value,
                evt.CompletedCount,
                evt.Timestamp);
        }

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
        _diagnosticStore.CompleteJob(jobId.Value, failed: false);
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
        _diagnosticStore.CompleteJob(jobId.Value, failed: true);
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

        // Write an SSE comment immediately to force HTTP response headers to the
        // client. Without this, Kestrel may defer header delivery until the first
        // real data write, causing the client's ResponseHeadersRead to block.
        await HttpContext.Response.WriteAsync(": stream-open\n\n", ct);
        await HttpContext.Response.Body.FlushAsync(ct);

        var (reader, writer) = _store.Subscribe(jobId);
        try
        {
            // Parse Last-Event-ID for SSE reconnect support.
            long lastEventId = 0;
            if (HttpContext.Request.Headers.TryGetValue("Last-Event-ID", out var lastIdHeader)
                && long.TryParse(lastIdHeader.ToString(), out var parsedId))
            {
                lastEventId = parsedId;
            }

            // Replay events from the ring buffer so a late-connecting client sees
            // recent history. Clients use the bootstrap endpoint for per-project state;
            // the ring buffer provides only the recent event stream.
            foreach (var pastEvt in _store.GetSnapshot(jobId))
            {
                if (pastEvt.EventSequence <= lastEventId) continue;
                var pastJson = JsonSerializer.Serialize(pastEvt, _jsonOptions);
                await HttpContext.Response.WriteAsync($"id: {pastEvt.EventSequence}\ndata: {pastJson}\n\n", ct);
            }
            await HttpContext.Response.Body.FlushAsync(ct);

            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();

            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, _jsonOptions);
                await HttpContext.Response.WriteAsync($"id: {evt.EventSequence}\ndata: {json}\n\n", ct);
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
