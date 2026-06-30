// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Custom <see cref="ILoggerProvider"/> that pushes <see cref="DiagnosticLogRecord"/>
/// batches to the control plane via <c>POST /agents/lease/{leaseId}/diagnostics</c>.
/// Uses an unbounded channel and <see cref="BackgroundService"/> drain loop.
/// Failures are counted silently — never propagated (circular dependency prevents ILogger use here).
/// </summary>
[ProviderAlias("ControlPlaneLogger")]
public sealed class ControlPlaneLoggerProvider : BackgroundService, ILoggerProvider
{
    internal const string HttpClientName = nameof(ControlPlaneLoggerProvider);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Channel<DiagnosticLogRecord> _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ActiveLeaseState _leaseState;
    private readonly LogLevel _minimumLevel;
    private readonly int _flushBatchSize;
    private readonly TimeSpan _flushInterval;
    private long _droppedCount;

    // Lazily resolved to break circular dependency:
    // ControlPlaneLoggerProvider (ILoggerProvider) -> IHttpClientFactory -> (resilience ILogger) -> ILoggerFactory -> ILoggerProvider
    private IHttpClientFactory? _httpFactory;

    // Lazily resolved: when registered, diagnostics are routed through UnifiedWorkerEventWriter
    // instead of posting directly to the old /diagnostics endpoint.
    private UnifiedWorkerEventWriter? _eventWriter;
    private bool _eventWriterResolved;

    public ControlPlaneLoggerProvider(
        IServiceProvider serviceProvider,
        ActiveLeaseState leaseState,
        IOptions<DiagnosticLogOptions> options)
    {
        _serviceProvider = serviceProvider;
        _leaseState = leaseState;

        var opts = options.Value;
        _minimumLevel = Enum.TryParse<LogLevel>(opts.MinimumLevel, ignoreCase: true, out var level)
            ? level
            : LogLevel.Information;
        _flushBatchSize = opts.FlushBatchSize;
        _flushInterval = TimeSpan.FromMilliseconds(opts.FlushIntervalMs);

        // Unbounded: ILogger.Log is synchronous so we cannot await backpressure here.
        // DropOldest silently discarded diagnostic records under any CP unavailability.
        // The batch flush loop drains this channel every FlushInterval ms; memory growth
        // is bounded by job duration. opts.ChannelCapacity is preserved for configuration
        // compatibility but no longer constrains the channel.
        _ = opts.ChannelCapacity; // retained in DiagnosticLogOptions for future use
        _channel = Channel.CreateUnbounded<DiagnosticLogRecord>();
    }

    private IHttpClientFactory HttpFactory => _httpFactory ??= _serviceProvider.GetRequiredService<IHttpClientFactory>();

    private UnifiedWorkerEventWriter? EventWriter
    {
        get
        {
            if (!_eventWriterResolved)
            {
                _eventWriter = _serviceProvider.GetService<UnifiedWorkerEventWriter>();
                _eventWriterResolved = true;
            }
            return _eventWriter;
        }
    }

    public ILogger CreateLogger(string categoryName)
        => new ControlPlaneLogger(this, categoryName);

    internal bool IsEnabled(LogLevel logLevel)
        => logLevel >= _minimumLevel;

    internal void Write(DiagnosticLogRecord record)
    {
        _channel.Writer.TryWrite(record);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<DiagnosticLogRecord>(_flushBatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                batch.Clear();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(_flushInterval);

                try
                {
                    while (batch.Count < _flushBatchSize)
                    {
                        var record = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                        batch.Add(record);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Flush interval elapsed or shutdown — flush what we have.
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        // Drain remaining on shutdown.
        batch.Clear();
        while (_channel.Reader.TryRead(out var remaining))
        {
            batch.Add(remaining);
        }
        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FlushBatchAsync(
        List<DiagnosticLogRecord> batch,
        CancellationToken cancellationToken)
    {
        // Prefer routing through UnifiedWorkerEventWriter (Phase C): it handles retries,
        // batching, and backpressure in one place. Fall back to the direct HTTP path only
        // when the writer is not registered (e.g. older deployment without Phase C).
        var writer = EventWriter;
        if (writer is not null)
        {
            writer.EnqueueDiagnostic(batch.ToArray());
            return;
        }

        // Legacy HTTP path (pre-Phase C fallback).
        var leaseId = _leaseState.CurrentLeaseId;
        if (string.IsNullOrEmpty(leaseId))
        {
            // No lease yet — drop silently.
            Interlocked.Add(ref _droppedCount, batch.Count);
            return;
        }

        try
        {
            using var http = HttpFactory.CreateClient(HttpClientName);
            var response = await http.PostAsJsonAsync(
                $"/agents/lease/{Uri.EscapeDataString(leaseId)}/diagnostics",
                batch,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Add(ref _droppedCount, batch.Count);
            }
        }
        catch (HttpRequestException)
        {
            Interlocked.Add(ref _droppedCount, batch.Count);
            // Best-effort — failures are silently counted.
            // Cannot use ILogger here: this class IS an ILoggerProvider,
            // so injecting ILogger<T> creates a circular dependency deadlock.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown — discard.
        }
    }

    /// <summary>
    /// Inner logger created per category. Maps <c>ILogger.Log</c> calls to
    /// <see cref="DiagnosticLogRecord"/> and writes to the provider's channel.
    /// </summary>
    private sealed class ControlPlaneLogger : ILogger
    {
        private readonly ControlPlaneLoggerProvider _provider;
        private readonly string _category;

        public ControlPlaneLogger(ControlPlaneLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public bool IsEnabled(LogLevel logLevel)
            => _provider.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var activity = Activity.Current;
            var classification = DataClassificationScope.Current;
            var record = new DiagnosticLogRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = logLevel.ToString(),
                Category = _category,
                Message = formatter(state, exception),
                Exception = exception?.ToString(),
                TraceId = activity?.TraceId.ToString(),
                SpanId = activity?.SpanId.ToString(),
                DataClassification = classification?.ToString()
            };

            _provider.Write(record);
        }
    }
}
