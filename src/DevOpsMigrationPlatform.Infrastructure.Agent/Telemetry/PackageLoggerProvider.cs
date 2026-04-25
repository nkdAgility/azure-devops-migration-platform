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
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Custom <see cref="ILoggerProvider"/> that captures <c>ILogger</c> output and writes it
/// as NDJSON <see cref="DiagnosticLogRecord"/> lines to <c>Logs/agent.jsonl</c> in the
/// migration package via <see cref="IArtefactStore"/>. Uses a bounded channel and
/// <see cref="BackgroundService"/> drain loop for non-blocking writes. Respects
/// <see cref="DiagnosticLogOptions.MinimumLevel"/>. The <see cref="IArtefactStore"/> is
/// resolved lazily from <see cref="ActivePackageState"/> because it is only available
/// after a job lease is acquired.
/// </summary>
[ProviderAlias("PackageLogger")]
public sealed class PackageLoggerProvider : BackgroundService, ILoggerProvider
{
    private const string LogBaseName = "agent";
    private const string LogExtension = ".jsonl";

    private readonly Channel<DiagnosticLogRecord> _channel;
    private readonly ActivePackageState _packageState;
    private readonly LogLevel _minimumLevel;
    private readonly int _flushBatchSize;
    private readonly TimeSpan _flushInterval;
    private readonly long _maxSegmentBytes;
    private long _droppedCount;
    private volatile IArtefactStore? _lastKnownStore;

    // Rotation state — only accessed from the drain loop (single-threaded).
    private int _segmentIndex;
    private long _currentSegmentBytes;

    public PackageLoggerProvider(
        ActivePackageState packageState,
        IOptions<DiagnosticLogOptions> options)
    {
        _packageState = packageState;
        var opts = options.Value;
        _minimumLevel = Enum.TryParse<LogLevel>(opts.MinimumLevel, ignoreCase: true, out var level) ? level : LogLevel.Information;
        _flushBatchSize = opts.FlushBatchSize;
        _flushInterval = TimeSpan.FromMilliseconds(opts.FlushIntervalMs);
        _maxSegmentBytes = opts.MaxLogFileSizeMB > 0
            ? (long)opts.MaxLogFileSizeMB * 1024 * 1024
            : long.MaxValue; // 0 = disabled

        _channel = Channel.CreateBounded<DiagnosticLogRecord>(
            new BoundedChannelOptions(opts.ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    /// <summary>
    /// Returns the current log segment path (e.g. <c>Logs/638...-jobId/agent.jsonl</c>,
    /// <c>Logs/638...-jobId/agent-001.jsonl</c>, etc.).
    /// </summary>
    internal string CurrentLogPath
    {
        get
        {
            var logDir = _packageState.CurrentLogFolder;
            return _segmentIndex == 0
                ? $"{logDir}/{LogBaseName}{LogExtension}"
                : $"{logDir}/{LogBaseName}-{_segmentIndex:D3}{LogExtension}";
        }
    }

    public ILogger CreateLogger(string categoryName)
        => new PackageLogger(this, categoryName);

    internal bool IsEnabled(LogLevel logLevel)
        => logLevel >= _minimumLevel;

    internal void Write(DiagnosticLogRecord record)
    {
        // Eagerly capture the store reference on the write path so the drain loop
        // can flush even if the job completes before the next poll interval.
        var store = _packageState.CurrentStore;
        if (store is not null)
            _lastKnownStore = store;

        _channel.Writer.TryWrite(record);
    }

    /// <summary>
    /// Drains all buffered records to the package store. Called by <c>JobAgentWorker</c>
    /// after a job completes but before <see cref="ActivePackageState.Clear"/> — ensures
    /// no records are lost when the process is terminated without graceful shutdown
    /// (e.g. process-per-component mode where the CLI kills child processes).
    /// </summary>
    public async Task FlushAsync()
    {
        var batch = new List<DiagnosticLogRecord>();
        while (_channel.Reader.TryRead(out var record))
            batch.Add(record);
        if (batch.Count > 0)
            await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<DiagnosticLogRecord>(_flushBatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for a store to become available before consuming from the channel.
                // Records stay safely buffered in the bounded channel while no job is active.
                var currentStore = _packageState.CurrentStore;
                if (currentStore is null)
                {
                    await Task.Delay(_flushInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }
                _lastKnownStore = currentStore;

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
                    // Use CancellationToken.None so that records are not dropped when
                    // stoppingToken fires while a batch is mid-flush. File.AppendAllTextAsync
                    // returns Task.FromCanceled immediately for a pre-cancelled token, silently
                    // discarding records. Log appends are fast local I/O and should always complete.
                    await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
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

            var payload = sb.ToString();
            var payloadBytes = Encoding.UTF8.GetByteCount(payload);

            // Rotate if this batch would exceed the segment limit.
            if (_currentSegmentBytes > 0
                && _currentSegmentBytes + payloadBytes > _maxSegmentBytes)
            {
                _segmentIndex++;
                _currentSegmentBytes = 0;
            }

            await store.AppendAsync(CurrentLogPath, payload, cancellationToken).ConfigureAwait(false);
            _currentSegmentBytes += payloadBytes;
        }
        catch (Exception)
        {
            Interlocked.Add(ref _droppedCount, batch.Count);
            // Best-effort — failures are counted but not propagated.
        }
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
#endif
