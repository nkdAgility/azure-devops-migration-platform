using System.Collections.Concurrent;
using System.Threading.Channels;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

public sealed class JobProgressStore
{
    private sealed class JobProgressEntry
    {
        public ConcurrentQueue<ProgressEvent> Queue { get; } = new();
        public List<ChannelWriter<ProgressEvent>> Subscribers { get; } = new();
        public bool Failed { get; set; }
        public bool Completed { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, JobProgressEntry> _entries = new();
    private readonly int _capacity;

    public JobProgressStore(IOptions<JobProgressOptions> options)
    {
        _capacity = options.Value.Capacity;
    }

    public void Append(Guid jobId, ProgressEvent evt)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobProgressEntry());
        entry.Queue.Enqueue(evt);
        while (entry.Queue.Count > _capacity)
            entry.Queue.TryDequeue(out _);

        lock (entry.Subscribers)
        {
            foreach (var writer in entry.Subscribers)
                writer.TryWrite(evt);
        }
    }

    public IReadOnlyList<ProgressEvent> GetSnapshot(Guid jobId)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return Array.Empty<ProgressEvent>();
        return entry.Queue.ToArray();
    }

    public (ChannelReader<ProgressEvent> Reader, ChannelWriter<ProgressEvent> Writer) Subscribe(Guid jobId)
    {
        var entry = _entries.GetOrAdd(jobId, _ => new JobProgressEntry());
        var channel = Channel.CreateBounded<ProgressEvent>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
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
        // GetOrAdd ensures the entry exists even when CompleteJob races ahead of any
        // Append call (e.g. ControlPlaneProgressSink drain loop hasn't fired yet).
        // Completed/Failed are set inside the lock so Subscribe cannot observe a
        // partially-initialised entry between GetOrAdd and the flag assignment.
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
    /// Returns the highest <see cref="ProgressEvent.EventSequence"/> seen for the job,
    /// or 0 if no events have been recorded. Used by the bootstrap endpoint.
    /// </summary>
    public long GetMaxEventSequence(Guid jobId)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return 0;

        long max = 0;
        foreach (var evt in entry.Queue)
        {
            if (evt.EventSequence > max)
                max = evt.EventSequence;
        }
        return max;
    }

    public void Remove(Guid jobId) =>
        _entries.TryRemove(jobId, out _);
}
