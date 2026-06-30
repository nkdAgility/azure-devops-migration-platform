// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Single unified flush path from agent to control plane.
/// Replaces the separate <see cref="ControlPlaneProgressSink"/> and
/// <see cref="ControlPlaneTelemetryClient"/> channels with one unbounded channel
/// and one background flush task.
/// <para>
/// Batch policy: up to 50 events or 500 ms (whichever comes first) per POST.
/// Terminal events bypass the timer and are flushed immediately.
/// On 429 the batch is retried after 2 s; on other failures exponential backoff
/// up to 5 attempts, then the batch is discarded with an error log.
/// </para>
/// </summary>
public sealed class UnifiedWorkerEventWriter : BackgroundService, IProgressSink, IFlushable
{
    internal const string HttpClientName = nameof(UnifiedWorkerEventWriter);

    private const int BatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);
    private const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Channel<WorkerEvent> _channel = Channel.CreateUnbounded<WorkerEvent>();
    private readonly IHttpClientFactory _httpFactory;
    private readonly ActiveLeaseState _leaseState;
    private readonly ILogger<UnifiedWorkerEventWriter> _logger;

    // Stable per-process identity; the CP uses this for routing/diagnostics only.
    public string WorkerId { get; } = Guid.NewGuid().ToString("N");

    private long _seq;

    // The background loop holds this for its entire read+flush cycle (including the
    // ReadAsync wait). FlushAsync() acquires it before draining the channel, which
    // guarantees it never races with a mid-flight batch the background loop has
    // already dequeued from the channel but not yet POST'd.
    private readonly SemaphoreSlim _cycleLock = new(1, 1);

    public UnifiedWorkerEventWriter(
        IHttpClientFactory httpFactory,
        ActiveLeaseState leaseState,
        ILogger<UnifiedWorkerEventWriter> logger)
    {
        _httpFactory = httpFactory;
        _leaseState = leaseState;
        _logger = logger;
    }

    // ── IProgressSink ────────────────────────────────────────────────────────

    public void Emit(ProgressEvent evt)
        => Enqueue(WorkerEventKind.Progress, evt);

    // ── IFlushable ───────────────────────────────────────────────────────────

    public async Task FlushAsync()
    {
        // Acquire the cycle lock so we wait for any in-progress background read+flush
        // to complete before draining.  The background loop holds this lock for its
        // entire cycle (ReadAsync + FlushWithRetryAsync), so by the time we acquire
        // it the channel is guaranteed to contain only events that haven't been sent.
        await _cycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var batch = new List<WorkerEvent>();
            while (_channel.Reader.TryRead(out var evt))
                batch.Add(evt);
            if (batch.Count > 0)
                await FlushWithRetryAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    // ── Internal enqueue API (used by ControlPlaneLoggerProvider and JobAgentWorker) ────

    internal void EnqueueDiagnostic(DiagnosticLogRecord[] records)
        => Enqueue(WorkerEventKind.Diagnostic, records);

    internal void EnqueueTerminal(bool failed)
        => EnqueueImmediate(WorkerEventKind.Terminal, new TerminalPayload(failed));

    public void EnqueueTasks(JobTaskList tasks)
        => Enqueue(WorkerEventKind.Tasks, tasks);

    // ── Drain loop ───────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Hold the cycle lock for the entire read+flush so FlushAsync() is
                // forced to wait for whichever phase we're currently in.
                await _cycleLock.WaitAsync(stoppingToken).ConfigureAwait(false);
                try
                {
                    var batch = new List<WorkerEvent>(BatchSize);

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    cts.CancelAfter(FlushInterval);

                    try
                    {
                        while (batch.Count < BatchSize)
                        {
                            var evt = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                            batch.Add(evt);

                            // Flush immediately on Terminal to avoid leaving it in the buffer.
                            if (evt.Kind == WorkerEventKind.Terminal)
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Flush interval elapsed or shutdown — flush what we have.
                    }

                    if (batch.Count > 0)
                        await FlushWithRetryAsync(batch, stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    _cycleLock.Release();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }

        // Drain remaining on shutdown.
        var shutdown = new List<WorkerEvent>(BatchSize);
        while (_channel.Reader.TryRead(out var remaining))
            shutdown.Add(remaining);
        if (shutdown.Count > 0)
            await FlushWithRetryAsync(shutdown, CancellationToken.None).ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Enqueue(WorkerEventKind kind, object payload)
    {
        var seq = Interlocked.Increment(ref _seq);
        var json = JsonSerializer.Serialize(payload, payload.GetType(), _jsonOptions);
        _channel.Writer.TryWrite(new WorkerEvent(seq, DateTimeOffset.UtcNow, kind, json));
    }

    private void EnqueueImmediate(WorkerEventKind kind, object payload)
        => Enqueue(kind, payload); // channel is unbounded so write is always sync

    private async Task FlushWithRetryAsync(List<WorkerEvent> batch, CancellationToken ct)
    {
        var leaseId = _leaseState.CurrentLeaseId;
        if (string.IsNullOrEmpty(leaseId))
            return; // No lease yet — drop silently (pre-job diagnostics).

        var eventBatch = new WorkerEventBatch(WorkerId, leaseId!, batch.AsReadOnly());
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var http = _httpFactory.CreateClient(HttpClientName);
                var response = await http
                    .PostAsJsonAsync($"/workers/{Uri.EscapeDataString(WorkerId)}/events", eventBatch, _jsonOptions, ct)
                    .ConfigureAwait(false);

                if ((int)response.StatusCode == 429) // TooManyRequests (not available in net481)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    continue; // Retry same batch.
                }

                if (response.IsSuccessStatusCode)
                    return;

                _logger.LogWarning(
                    "Worker event batch POST returned {StatusCode} (attempt {Attempt}/{Max}).",
                    (int)response.StatusCode, attempt, MaxAttempts);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Worker event batch POST failed (attempt {Attempt}/{Max}).", attempt, MaxAttempts);
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
        }

        _logger.LogError(
            "Worker event batch of {Count} events discarded after {Max} failed attempts.",
            batch.Count, MaxAttempts);
    }

    private sealed record TerminalPayload(bool Failed);
}
