using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// In-memory ring buffer for <see cref="DiagnosticLogRecord"/> per job.
/// Mirrors <see cref="JobProgressStore"/> pattern: ring buffer + SSE subscribers.
/// Records below the deployment-level minimum log level are discarded on ingestion.
/// </summary>
public sealed class DiagnosticLogStore
{
    private sealed class JobEntry
    {
        public ConcurrentQueue<DiagnosticLogRecord> Queue { get; } = new();
        public List<ChannelWriter<DiagnosticLogRecord>> Subscribers { get; } = new();
        public bool Completed { get; set; }
        public bool Failed { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, JobEntry> _entries = new();
    private readonly int _capacity;
    private readonly LogLevel _minimumLevel;

    public DiagnosticLogStore(IOptions<DiagnosticLogStoreOptions> options)
    {
        _capacity = options.Value.Capacity;
        _minimumLevel = Enum.TryParse<LogLevel>(options.Value.MinimumLevel, ignoreCase: true, out var level)
            ? level
            : LogLevel.Warning;
    }

    /// <summary>
    /// Adds records to the ring buffer for the given job, filtering by deployment-level minimum.
    /// Notifies all SSE subscribers.
    /// </summary>
    public void Add(Guid jobId, IEnumerable<DiagnosticLogRecord> records)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobEntry());

        foreach (var record in records)
        {
            if (!Enum.TryParse<LogLevel>(record.Level, ignoreCase: true, out var recordLevel))
                continue;

            if (recordLevel < _minimumLevel)
                continue;

            entry.Queue.Enqueue(record);
            while (entry.Queue.Count > _capacity)
                entry.Queue.TryDequeue(out _);

            lock (entry.Subscribers)
            {
                foreach (var writer in entry.Subscribers)
                    writer.TryWrite(record);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of buffered records, optionally filtered by level.
    /// </summary>
    public IReadOnlyList<DiagnosticLogRecord> GetSnapshot(Guid jobId, LogLevel? levelFilter = null)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return Array.Empty<DiagnosticLogRecord>();

        if (levelFilter is null)
            return entry.Queue.ToArray();

        return entry.Queue
            .Where(r => Enum.TryParse<LogLevel>(r.Level, ignoreCase: true, out var rl) && rl >= levelFilter.Value)
            .ToArray();
    }

    /// <summary>
    /// Subscribes to the live diagnostic stream for a job.
    /// </summary>
    public (ChannelReader<DiagnosticLogRecord> Reader, ChannelWriter<DiagnosticLogRecord> Writer) Subscribe(Guid jobId)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobEntry());
        var channel = Channel.CreateBounded<DiagnosticLogRecord>(
            new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (entry.Subscribers)
        {
            if (entry.Completed)
            {
                // Job already finished before this subscriber connected — pre-complete
                // the channel so ReadAllAsync returns immediately and SSE sends job-ended.
                channel.Writer.TryComplete();
            }
            else
            {
                entry.Subscribers.Add(channel.Writer);
            }
        }
        return (channel.Reader, channel.Writer);
    }

    public void Unsubscribe(Guid jobId, ChannelWriter<DiagnosticLogRecord> writer)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return;
        lock (entry.Subscribers)
        {
            entry.Subscribers.Remove(writer);
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Completes subscriber channels for a finished job.
    /// </summary>
    public void CompleteJob(Guid jobId, bool failed = false)
    {
        // GetOrAdd ensures the entry exists even when CompleteJob races ahead of any
        // Add call (e.g. agent signals complete before the first diagnostic POST arrives).
        var entry = _entries.GetOrAdd(jobId, _ => new JobEntry());
        lock (entry.Subscribers)
        {
            entry.Completed = true;
            entry.Failed = failed;
            foreach (var writer in entry.Subscribers)
                writer.TryComplete();
            entry.Subscribers.Clear();
        }
    }

    public bool WasFailed(Guid jobId) =>
        _entries.TryGetValue(jobId, out var e) && e.Failed;

    public bool IsCompleted(Guid jobId) =>
        _entries.TryGetValue(jobId, out var e) && e.Completed;
}
