// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Append-only log of <see cref="DiagnosticLogRecord"/> per job, replacing the former
/// ring buffer. Subscribers receive live records via SSE channels as before.
/// Records below the deployment-level minimum log level are discarded on ingestion.
/// </summary>
public sealed class DiagnosticLogStore
{
    private sealed class JobEntry : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly List<DiagnosticLogRecord> _log = new();
        public List<ChannelWriter<DiagnosticLogRecord>> Subscribers { get; } = new();
        public bool Completed { get; set; }
        public bool Failed { get; set; }

        public void Append(DiagnosticLogRecord record)
        {
            _lock.EnterWriteLock();
            try { _log.Add(record); }
            finally { _lock.ExitWriteLock(); }
        }

        public DiagnosticLogRecord[] Snapshot(LogLevel? levelFilter = null)
        {
            _lock.EnterReadLock();
            try
            {
                if (levelFilter is null)
                    return _log.ToArray();
                return _log
                    .Where(r => Enum.TryParse<LogLevel>(r.Level, ignoreCase: true, out var rl) && rl >= levelFilter.Value)
                    .ToArray();
            }
            finally { _lock.ExitReadLock(); }
        }

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try { return _log.Count; }
                finally { _lock.ExitReadLock(); }
            }
        }

        public void Dispose() => _lock.Dispose();
    }

    private readonly ConcurrentDictionary<Guid, JobEntry> _entries = new();
    private readonly int _maxRecordsPerJob;
    private readonly LogLevel _minimumLevel;
    private readonly ILogger<DiagnosticLogStore>? _logger;

    public DiagnosticLogStore(IOptions<DiagnosticLogStoreOptions> options, ILogger<DiagnosticLogStore>? logger = null)
    {
        var opts = options.Value;
        _maxRecordsPerJob = opts.MaxRecordsPerJob > 0 ? opts.MaxRecordsPerJob : 50_000;
        _minimumLevel = Enum.TryParse<LogLevel>(opts.MinimumLevel, ignoreCase: true, out var level)
            ? level
            : LogLevel.Warning;
        _logger = logger;
    }

    /// <summary>
    /// Adds records to the log for the given job, filtering by deployment-level minimum.
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

            if (entry.Count >= _maxRecordsPerJob)
            {
                _logger?.LogWarning(
                    "Job {JobId} has reached {Max} diagnostic records. Further records are discarded.",
                    jobId, _maxRecordsPerJob);
                return;
            }

            entry.Append(record);

            lock (entry.Subscribers)
            {
                foreach (var writer in entry.Subscribers)
                    writer.TryWrite(record);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of all stored records, optionally filtered by level.
    /// </summary>
    public IReadOnlyList<DiagnosticLogRecord> GetSnapshot(Guid jobId, LogLevel? levelFilter = null)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return Array.Empty<DiagnosticLogRecord>();
        return entry.Snapshot(levelFilter);
    }

    /// <summary>
    /// Subscribes to the live diagnostic stream for a job.
    /// </summary>
    public (ChannelReader<DiagnosticLogRecord> Reader, ChannelWriter<DiagnosticLogRecord> Writer) Subscribe(Guid jobId)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobEntry());
        // DropOldest keeps the terminal event reachable under slow-client backpressure.
        // Phase D's append-only log provides full replay on reconnect.
        var channel = Channel.CreateBounded<DiagnosticLogRecord>(
            new BoundedChannelOptions(5_000) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (entry.Subscribers)
        {
            if (entry.Completed)
            {
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
