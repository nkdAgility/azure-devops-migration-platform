#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Custom <see cref="ILoggerProvider"/> that captures <c>ILogger</c> output and writes it
/// as NDJSON <see cref="DiagnosticLogRecord"/> lines to <c>Logs/agent.jsonl</c> in the
/// migration package via <see cref="IArtefactStore"/>. Uses a bounded channel and
/// background drain loop for non-blocking writes. Respects <see cref="DiagnosticLogOptions.MinimumLevel"/>.
/// The <see cref="IArtefactStore"/> is resolved lazily from <see cref="ActivePackageState"/>
/// because it is only available after a job lease is acquired.
/// </summary>
[ProviderAlias("PackageLogger")]
public sealed class PackageLoggerProvider : ILoggerProvider, IDisposable
{
    private const string LogPath = "Logs/agent.jsonl";

    private readonly Channel<DiagnosticLogRecord> _channel;
    private readonly ActivePackageState _packageState;
    private readonly LogLevel _minimumLevel;
    private readonly int _flushBatchSize;
    private readonly TimeSpan _flushInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _drainTask;
    private long _droppedCount;
    private volatile IArtefactStore? _lastKnownStore;

    public PackageLoggerProvider(
        ActivePackageState packageState,
        IOptions<DiagnosticLogOptions> options)
    {
        _packageState = packageState;
        var opts = options.Value;
        _minimumLevel = Enum.TryParse<LogLevel>(opts.MinimumLevel, ignoreCase: true, out var level) ? level : LogLevel.Information;
        _flushBatchSize = opts.FlushBatchSize;
        _flushInterval = TimeSpan.FromMilliseconds(opts.FlushIntervalMs);

        _channel = Channel.CreateBounded<DiagnosticLogRecord>(
            new BoundedChannelOptions(opts.ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

        _drainTask = DrainLoopAsync(_cts.Token);
    }

    public ILogger CreateLogger(string categoryName)
        => new PackageLogger(this, categoryName);

    internal bool IsEnabled(LogLevel logLevel)
        => logLevel >= _minimumLevel;

    internal void Write(DiagnosticLogRecord record)
    {
        _channel.Writer.TryWrite(record);
    }

    private async Task DrainLoopAsync(CancellationToken cancellationToken)
    {
        var batch = new List<DiagnosticLogRecord>(_flushBatchSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for a store to become available before consuming from the channel.
                // Records stay safely buffered in the bounded channel while no job is active.
                var currentStore = _packageState.CurrentStore;
                if (currentStore is null)
                {
                    await Task.Delay(_flushInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                _lastKnownStore = currentStore;

                batch.Clear();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                    await FlushBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    private async Task FlushBatchAsync(List<DiagnosticLogRecord> batch, CancellationToken cancellationToken)
    {
        // Prefer the live store; fall back to the last known reference so the
        // shutdown drain can still flush after MigrationAgentWorker clears the state.
        var store = _packageState.CurrentStore ?? _lastKnownStore;
        if (store is null)
        {
            // No store has ever been set — count as dropped.
            Interlocked.Add(ref _droppedCount, batch.Count);
            return;
        }

        try
        {
            var sb = new StringBuilder();
            foreach (var record in batch)
            {
                sb.AppendLine(JsonSerializer.Serialize(record));
            }
            await store.AppendAsync(LogPath, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Interlocked.Add(ref _droppedCount, batch.Count);
            // Best-effort — failures are counted but not propagated.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        // Allow drain to complete (bounded wait to avoid blocking disposal indefinitely).
        _drainTask.Wait(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    /// <summary>
    /// Inner logger created per category. Maps <c>ILogger.Log</c> calls to
    /// <see cref="DiagnosticLogRecord"/> and writes to the provider's channel.
    /// </summary>
    private sealed class PackageLogger : ILogger
    {
        private readonly PackageLoggerProvider _provider;
        private readonly string _category;

        public PackageLogger(PackageLoggerProvider provider, string category)
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
            var record = new DiagnosticLogRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = logLevel.ToString(),
                Category = _category,
                Message = formatter(state, exception),
                Exception = exception?.ToString(),
                TraceId = activity?.TraceId.ToString(),
                SpanId = activity?.SpanId.ToString()
            };

            _provider.Write(record);
        }
    }
}
#endif
