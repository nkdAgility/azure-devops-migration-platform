using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

[ApiController]
public sealed class DiagnosticsController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly DiagnosticLogStore _store;
    private readonly ILeaseJobResolver _resolver;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        DiagnosticLogStore store,
        ILeaseJobResolver resolver,
        ILogger<DiagnosticsController> logger)
    {
        _store = store;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Agent pushes a batch of <see cref="DiagnosticLogRecord"/> for an active lease.
    /// <c>POST /agents/lease/{leaseId}/diagnostics</c>
    /// </summary>
    [HttpPost("/agents/lease/{leaseId}/diagnostics")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult PostDiagnostics(string leaseId, [FromBody] List<DiagnosticLogRecord> records)
    {
        var jobId = _resolver.ResolveJobId(leaseId);
        if (jobId is null)
            return NotFound($"Lease '{leaseId}' is not recognised.");

        _store.Add(jobId.Value, records);
        return NoContent();
    }

    /// <summary>
    /// Returns a snapshot of diagnostic records, or streams them via SSE when
    /// <c>follow=true</c>. Supports optional <c>level</c> filter.
    /// <c>GET /jobs/{jobId}/diagnostics</c>
    /// </summary>
    [HttpGet("/jobs/{jobId}/diagnostics")]
    [ProducesResponseType(200)]
    public async Task GetDiagnostics(
        Guid jobId,
        [FromQuery] bool follow = false,
        [FromQuery] string? level = null,
        CancellationToken ct = default)
    {
        LogLevel? levelFilter = null;
        if (!string.IsNullOrEmpty(level) && Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed))
        {
            levelFilter = parsed;
        }

        if (!follow)
        {
            var snapshot = _store.GetSnapshot(jobId, levelFilter);
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            await HttpContext.Response.WriteAsync(
                JsonSerializer.Serialize(snapshot, _jsonOptions), ct);
            return;
        }

        // SSE stream
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
            // Replay buffered records.
            foreach (var pastRecord in _store.GetSnapshot(jobId, levelFilter))
            {
                var pastJson = JsonSerializer.Serialize(pastRecord, _jsonOptions);
                await HttpContext.Response.WriteAsync($"data: {pastJson}\n\n", ct);
            }
            await HttpContext.Response.Body.FlushAsync(ct);

            await foreach (var record in reader.ReadAllAsync(ct))
            {
                // Apply client-side level filter on the SSE stream.
                if (levelFilter is not null
                    && Enum.TryParse<LogLevel>(record.Level, ignoreCase: true, out var rl)
                    && rl < levelFilter.Value)
                {
                    continue;
                }

                var json = JsonSerializer.Serialize(record, _jsonOptions);
                await HttpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                await HttpContext.Response.Body.FlushAsync(ct);
            }

            // Send terminal event.
            await HttpContext.Response.WriteAsync(
                _store.WasFailed(jobId)
                    ? "event: job-failed\ndata: {}\n\n"
                    : "event: job-ended\ndata: {}\n\n", ct);
            await HttpContext.Response.Body.FlushAsync(ct);
        }
        finally
        {
            _store.Unsubscribe(jobId, writer);
        }
    }
}
