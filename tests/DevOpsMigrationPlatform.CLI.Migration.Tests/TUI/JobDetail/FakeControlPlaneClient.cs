// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// In-memory <see cref="IControlPlaneClient"/> that delegates <see cref="StreamJobAsync"/>
/// to a <see cref="FakeSseServer"/>. Tracks stream call count for assertion.
/// </summary>
public sealed class FakeControlPlaneClient : IControlPlaneClient
{
    /// <summary>The SSE event source used by <see cref="StreamJobAsync"/>.</summary>
    public FakeSseServer SseServer { get; } = new();

    /// <summary>
    /// Number of times <see cref="StreamJobAsync"/> has been entered.
    /// Each mode switch (Tab press) re-enters the stream, so this increments on every mode.
    /// Tests asserting "Diagnostics mode was entered" check this is &gt;= 2 (Trace=1, Diagnostics=2).
    /// </summary>
    public int DiagnosticsStreamCallCount { get; private set; }

    /// <summary>Jobs returned by <see cref="GetAllJobsAsync"/>.</summary>
    public List<JobSummary> Jobs { get; } = new();

    /// <summary>Push a DiagnosticLogRecord into the fake stream.</summary>
    public void PushDiagnosticRecord(DiagnosticLogRecord record)
        => SseServer.Push(record);

    /// <summary>Complete the fake stream cleanly.</summary>
    public void CompleteDiagnosticsStream()
        => SseServer.CompleteStream();

    // ── IControlPlaneClient ───────────────────────────────────────────────────

    public Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<JobSummary>>(Jobs);

    public async IAsyncEnumerable<JobStreamEvent> StreamJobAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct,
        long fromSeq = 0)
    {
        DiagnosticsStreamCallCount++;
        await foreach (var evt in SseServer.GetEventsAsync(jobId, ct).ConfigureAwait(false))
            yield return evt;
    }

    public Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<JobBootstrap?>(null);

    public Task<JobTaskList?> GetTasksAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<JobTaskList?>(null);
}
