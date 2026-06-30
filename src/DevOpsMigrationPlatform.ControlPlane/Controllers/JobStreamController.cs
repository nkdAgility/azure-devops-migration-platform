// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Provides a unified SSE stream that multiplexes progress events and diagnostic
/// records into a single connection. The CLI subscribes here instead of opening
/// two separate SSE connections plus a polling loop.
///
/// <c>GET /jobs/{jobId}/stream?from={seq}</c>
///
/// Replays all stored events with sequence > from on connect (uses the append-only
/// log from Phase D), then switches to live subscriber channels.
/// Sends an SSE heartbeat comment every 15 s.
/// Closes with <c>event: job-ended</c> or <c>event: job-failed</c> on completion.
/// </summary>
[ApiController]
public sealed class JobStreamController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly JobProgressStore _progressStore;
    private readonly DiagnosticLogStore _diagnosticStore;

    public JobStreamController(
        JobProgressStore progressStore,
        DiagnosticLogStore diagnosticStore)
    {
        _progressStore = progressStore;
        _diagnosticStore = diagnosticStore;
    }

    [HttpGet("/jobs/{jobId}/stream")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    public async Task StreamJob(Guid jobId, [FromQuery] long from = 0, CancellationToken ct = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        HttpContext.Response.ContentType = "text/event-stream";
        HttpContext.Response.Headers["Cache-Control"] = "no-cache";
        HttpContext.Response.Headers["X-Accel-Buffering"] = "no";

        // Force headers to client immediately.
        await HttpContext.Response.WriteAsync(": stream-open\n\n", ct);
        await HttpContext.Response.Body.FlushAsync(ct);

        // Subscribe before replaying history so no live events are missed between
        // the snapshot read and the subscribe call.
        var (progressReader, progressWriter) = _progressStore.Subscribe(jobId);
        var (diagnosticReader, diagnosticWriter) = _diagnosticStore.Subscribe(jobId);

        try
        {
            // Replay stored progress events since the client's last seq.
            long seq = from;
            foreach (var evt in _progressStore.GetSnapshot(jobId, from))
            {
                var json = JsonSerializer.Serialize(evt, _jsonOptions);
                await HttpContext.Response.WriteAsync(
                    $"id: {evt.EventSequence}\nevent: progress\ndata: {json}\n\n", ct);
                if (evt.EventSequence > seq) seq = evt.EventSequence;
            }

            // Replay stored diagnostic records (no sequence numbers — send all).
            foreach (var record in _diagnosticStore.GetSnapshot(jobId))
            {
                var json = JsonSerializer.Serialize(record, _jsonOptions);
                await HttpContext.Response.WriteAsync($"event: diagnostic\ndata: {json}\n\n", ct);
            }

            await HttpContext.Response.Body.FlushAsync(ct);

            // If the job is already complete, write the terminal event and exit.
            if (_progressStore.WasFailed(jobId) || !_progressStore.WasFailed(jobId) &&
                IsCompleted(jobId))
            {
                await WriteTerminalAsync(jobId, ct);
                return;
            }

            // Multiplex live events from both subscriber channels.
            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();

            // Read progress and diagnostic channels concurrently via Task.WhenAny.
            var progressTask = progressReader.ReadAsync(ct).AsTask();
            var diagnosticTask = diagnosticReader.ReadAsync(ct).AsTask();

            while (!ct.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(progressTask, diagnosticTask, heartbeatTask);

                if (completed == progressTask)
                {
                    if (progressTask.IsCompletedSuccessfully)
                    {
                        var evt = progressTask.Result;
                        var json = JsonSerializer.Serialize(evt, _jsonOptions);
                        await HttpContext.Response.WriteAsync(
                            $"id: {evt.EventSequence}\nevent: progress\ndata: {json}\n\n", ct);
                        await HttpContext.Response.Body.FlushAsync(ct);
                        progressTask = progressReader.ReadAsync(ct).AsTask();
                    }
                    else
                    {
                        // Progress channel completed — job is done.
                        // Drain any remaining diagnostics.
                        await DrainDiagnosticsAsync(diagnosticReader, ct);
                        await WriteTerminalAsync(jobId, ct);
                        return;
                    }
                }
                else if (completed == diagnosticTask)
                {
                    if (diagnosticTask.IsCompletedSuccessfully)
                    {
                        var record = diagnosticTask.Result;
                        var json = JsonSerializer.Serialize(record, _jsonOptions);
                        await HttpContext.Response.WriteAsync($"event: diagnostic\ndata: {json}\n\n", ct);
                        await HttpContext.Response.Body.FlushAsync(ct);
                        diagnosticTask = diagnosticReader.ReadAsync(ct).AsTask();
                    }
                    else
                    {
                        diagnosticTask = Task.FromCanceled<DiagnosticLogRecord>(ct);
                    }
                }
                else // heartbeat
                {
                    await HttpContext.Response.WriteAsync(":\n\n", ct);
                    await HttpContext.Response.Body.FlushAsync(ct);
                    heartbeatTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal SSE teardown.
        }
        finally
        {
            _progressStore.Unsubscribe(jobId, progressWriter);
            _diagnosticStore.Unsubscribe(jobId, diagnosticWriter);
        }
    }

    private bool IsCompleted(Guid jobId) =>
        _diagnosticStore.IsCompleted(jobId);

    private async Task WriteTerminalAsync(Guid jobId, CancellationToken ct)
    {
        var failed = _progressStore.WasFailed(jobId);
        await HttpContext.Response.WriteAsync(
            failed ? "event: job-failed\ndata: {}\n\n" : "event: job-ended\ndata: {}\n\n", ct);
        await HttpContext.Response.Body.FlushAsync(ct);
    }

    private async Task DrainDiagnosticsAsync(
        System.Threading.Channels.ChannelReader<DiagnosticLogRecord> reader,
        CancellationToken ct)
    {
        while (reader.TryRead(out var record))
        {
            var json = JsonSerializer.Serialize(record, _jsonOptions);
            await HttpContext.Response.WriteAsync($"event: diagnostic\ndata: {json}\n\n", ct);
        }
        await HttpContext.Response.Body.FlushAsync(ct);
    }
}
