// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// Controllable SSE event source for log-stream tests.
/// Supports event queuing, connection drops, and subscription tracking.
/// </summary>
public sealed class FakeSseServer
{
    private Channel<JobStreamEvent> _events = Channel.CreateUnbounded<JobStreamEvent>();
    private int _subscriptionCount;
    private int _reconnectAttemptCount;
    private readonly List<CancellationToken> _issuedTokens = new();
    private bool _firstConnection = true;
    private long _seq;

    /// <summary>Number of currently active SSE subscribers.</summary>
    public int ActiveSubscriptionCount => _subscriptionCount;

    /// <summary>Number of times <see cref="GetEventsAsync"/> was called after the first call (i.e. reconnect attempts).</summary>
    public int ReconnectAttemptCount => _reconnectAttemptCount;

    /// <summary>All <see cref="CancellationToken"/>s that have been issued to SSE consumers.</summary>
    public IReadOnlyList<CancellationToken> IssuedTokens => _issuedTokens;

    /// <summary>Enqueue a progress event to be delivered to the current subscriber.</summary>
    public void Push(ProgressEvent evt)
        => _events.Writer.TryWrite(new JobStreamEvent(
            Interlocked.Increment(ref _seq), JobStreamEventKind.Progress, evt, null, null, null));

    /// <summary>Enqueue a diagnostic event to be delivered to the current subscriber.</summary>
    public void Push(DiagnosticLogRecord rec)
        => _events.Writer.TryWrite(new JobStreamEvent(
            Interlocked.Increment(ref _seq), JobStreamEventKind.Diagnostic, null, rec, null, null));

    /// <summary>Enqueue a terminal event signalling job completion.</summary>
    public void PushTerminal(bool failed = false)
        => _events.Writer.TryWrite(new JobStreamEvent(
            Interlocked.Increment(ref _seq), JobStreamEventKind.Terminal, null, null, failed, null));

    /// <summary>
    /// Drop the current connection by completing the current channel writer with an error and
    /// replacing it with a fresh one. Subsequent calls to <see cref="GetEventsAsync"/> will
    /// increment <see cref="ReconnectAttemptCount"/>.
    /// The completed iteration will throw <see cref="IOException"/> to simulate a network drop.
    /// </summary>
    public void DropConnection()
    {
        var old = _events;
        _events = Channel.CreateUnbounded<JobStreamEvent>();
        old.Writer.TryComplete(new IOException("Simulated SSE connection drop."));
    }

    /// <summary>Mark the stream as completed cleanly (terminal state — no reconnect expected).</summary>
    public void CompleteStream() => _events.Writer.TryComplete();

    /// <summary>Returns an async-enumerable stream of events for the given job, tracking subscriptions.</summary>
    public async IAsyncEnumerable<JobStreamEvent> GetEventsAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_firstConnection)
            Interlocked.Increment(ref _reconnectAttemptCount);
        _firstConnection = false;

        Interlocked.Increment(ref _subscriptionCount);
        _issuedTokens.Add(ct);
        try
        {
            await foreach (var evt in _events.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return evt;
        }
        finally
        {
            Interlocked.Decrement(ref _subscriptionCount);
        }
    }
}
