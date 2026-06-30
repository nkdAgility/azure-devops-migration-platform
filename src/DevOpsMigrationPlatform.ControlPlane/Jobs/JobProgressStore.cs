// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

public sealed class JobProgressStore
{
    private sealed class JobProgressEntry : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly List<ProgressEvent> _log = new();
        public List<ChannelWriter<ProgressEvent>> Subscribers { get; } = new();
        public bool Failed { get; set; }
        public bool Completed { get; set; }
        public long MaxSeq { get; private set; }

        public void Append(ProgressEvent evt)
        {
            _lock.EnterWriteLock();
            try
            {
                _log.Add(evt);
                if (evt.EventSequence > MaxSeq)
                    MaxSeq = evt.EventSequence;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public ProgressEvent[] Snapshot(long fromSeq = 0)
        {
            _lock.EnterReadLock();
            try
            {
                if (fromSeq <= 0)
                    return _log.ToArray();

                var result = new List<ProgressEvent>();
                foreach (var e in _log)
                    if (e.EventSequence > fromSeq)
                        result.Add(e);
                return result.ToArray();
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

    private readonly ConcurrentDictionary<Guid, JobProgressEntry> _entries = new();
    private readonly int _maxEventsPerJob;
    private readonly ILogger<JobProgressStore>? _logger;

    public JobProgressStore(IOptions<JobProgressOptions> options, ILogger<JobProgressStore>? logger = null)
    {
        _maxEventsPerJob = options.Value.MaxEventsPerJob > 0 ? options.Value.MaxEventsPerJob : 50_000;
        _logger = logger;
    }

    public void Append(Guid jobId, ProgressEvent evt)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobProgressEntry());

        if (entry.Count >= _maxEventsPerJob)
        {
            _logger?.LogWarning(
                "Job {JobId} has reached {Max} progress events. Further events are discarded.",
                jobId, _maxEventsPerJob);
            return;
        }

        entry.Append(evt);

        lock (entry.Subscribers)
        {
            foreach (var writer in entry.Subscribers)
                writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Returns all events with <see cref="ProgressEvent.EventSequence"/> greater than
    /// <paramref name="fromSeq"/>. Pass 0 (default) for the full history.
    /// </summary>
    public IReadOnlyList<ProgressEvent> GetSnapshot(Guid jobId, long fromSeq = 0)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return Array.Empty<ProgressEvent>();
        return entry.Snapshot(fromSeq);
    }

    public (ChannelReader<ProgressEvent> Reader, ChannelWriter<ProgressEvent> Writer) Subscribe(Guid jobId)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobProgressEntry());
        // Subscriber capacity is 5× the safety cap default; DropOldest keeps the most
        // recent events — including the terminal event — under slow-client backpressure.
        // Phase D's append-only log makes reconnect-replay reliable, so this path is rare.
        var channel = Channel.CreateBounded<ProgressEvent>(new BoundedChannelOptions(5_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
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

    public void Unsubscribe(Guid jobId, ChannelWriter<ProgressEvent> writer)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return;
        lock (entry.Subscribers)
        {
            entry.Subscribers.Remove(writer);
            writer.TryComplete();
        }
    }

    public void CompleteJob(Guid jobId, bool failed = false)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobProgressEntry());
        lock (entry.Subscribers)
        {
            entry.Failed = failed;
            entry.Completed = true;
            foreach (var writer in entry.Subscribers)
                writer.TryComplete();
            entry.Subscribers.Clear();
        }
    }

    public bool WasFailed(Guid jobId) =>
        _entries.TryGetValue(jobId, out var e) && e.Failed;

    /// <summary>
    /// Returns the highest <see cref="ProgressEvent.EventSequence"/> seen for the job, or 0.
    /// O(1) — tracked as a field on the entry.
    /// </summary>
    public long GetMaxEventSequence(Guid jobId) =>
        _entries.TryGetValue(jobId, out var entry) ? entry.MaxSeq : 0;

    public void Remove(Guid jobId)
    {
        if (_entries.TryRemove(jobId, out var entry))
            entry.Dispose();
    }
}
