// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Serves job progress reads (<c>GET /jobs/{jobId}/progress</c>, snapshot or SSE)
/// and the agent lease heartbeat. Progress data arrives via the unified
/// <c>POST /workers/{workerId}/events</c> channel.
/// </summary>
[ApiController]
public sealed class ProgressController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly JobProgressStore _store;
    private readonly ILeaseJobResolver _resolver;

    public ProgressController(
        JobProgressStore store,
        ILeaseJobResolver resolver)
    {
        _store = store;
        _resolver = resolver;
    }

    /// <summary>
    /// Agent sends a periodic liveness signal while a job is running.
    /// <c>POST /agents/lease/{leaseId}/heartbeat</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/heartbeat")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult Heartbeat(string leaseId)
    {
        if (!_resolver.RecordHeartbeat(leaseId))
            return NotFound($"Lease '{leaseId}' is not recognised.");
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
