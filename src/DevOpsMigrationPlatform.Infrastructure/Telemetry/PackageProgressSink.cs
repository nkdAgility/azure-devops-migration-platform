#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Writes <see cref="ProgressEvent"/> records to the migration package log file
/// (<c>Logs/progress.jsonl</c>) via <see cref="IArtefactStore"/>.
/// Uses a bounded channel and background drain loop following the same pattern
/// as <see cref="ControlPlaneProgressSink"/>. The <see cref="IArtefactStore"/> is resolved
/// lazily from <see cref="ActivePackageState"/> because it is only available after a
/// job lease is acquired.
/// </summary>
public sealed class PackageProgressSink : BackgroundService, IProgressSink
{
    private const int ChannelCapacity = 100;
    private const int FlushBatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);
    private const string LogFileName = "progress.jsonl";

    private readonly Channel<ProgressEvent> _channel = Channel.CreateBounded<ProgressEvent>(
        new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly ActivePackageState _packageState;
    private readonly ILogger<PackageProgressSink> _logger;
    private long _droppedCount;
    private volatile IArtefactStore? _lastKnownStore;

    public PackageProgressSink(
        ActivePackageState packageState,
        ILogger<PackageProgressSink> logger)
    {
        _packageState = packageState;
        _logger = logger;
    }

    public void Emit(ProgressEvent evt)
    {
        // Eagerly capture the store reference on the emit path so the drain loop
        // can flush even if the job completes before the next poll interval.
        var store = _packageState.CurrentStore;
        if (store is not null)
            _lastKnownStore = store;

        _channel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Drains all buffered records to the package store. Called by <c>JobAgentWorker</c>
    /// after a job completes but before <see cref="ActivePackageState.Clear"/> — ensures
    /// no records are lost when the process is terminated without graceful shutdown
    /// (e.g. process-per-component mode where the CLI kills child processes).
    /// </summary>
    public async Task FlushAsync()
    {
        var batch = new List<ProgressEvent>();
        while (_channel.Reader.TryRead(out var evt))
            batch.Add(evt);
        if (batch.Count > 0)
            await FlushBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ProgressEvent>(FlushBatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for a store to become available before consuming from the channel.
                // Records stay safely buffered in the bounded channel while no job is active.
                var currentStore = _packageState.CurrentStore;
                if (currentStore is null)
                {
                    await Task.Delay(FlushInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }
                _lastKnownStore = currentStore;

                batch.Clear();

                // Wait for at least one item, or flush interval — whichever comes first.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(FlushInterval);

                try
                {
                    while (batch.Count < FlushBatchSize)
                    {
                        var evt = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                        batch.Add(evt);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Either flush interval elapsed or host shutdown — flush what we have.
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
            // Normal shutdown — drain remaining records.
        }

        // Drain any remaining buffered records on shutdown.
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

    private async Task FlushBatchAsync(List<ProgressEvent> batch, CancellationToken cancellationToken)
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
            foreach (var evt in batch)
            {
                sb.AppendLine(JsonSerializer.Serialize(evt));
            }
            var logPath = $"{_packageState.CurrentLogFolder}/{LogFileName}";
            await store.AppendAsync(logPath, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Interlocked.Add(ref _droppedCount, batch.Count);
            _logger.LogDebug(ex,
                "Failed to write {Count} progress records to package. Total dropped: {DroppedCount}.",
                batch.Count, Interlocked.Read(ref _droppedCount));
        }
    }
}
#endif
