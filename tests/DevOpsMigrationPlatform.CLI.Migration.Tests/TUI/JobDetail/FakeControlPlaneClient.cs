// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// In-memory <see cref="IControlPlaneClient"/> that delegates <see cref="FollowLogsAsync"/>
/// to a <see cref="FakeSseServer"/>. Also tracks <see cref="GetTelemetryAsync"/> call
/// count and request paths for assertion.
/// </summary>
public sealed class FakeControlPlaneClient : IControlPlaneClient
{
    /// <summary>The SSE event source used by <see cref="FollowLogsAsync"/>.</summary>
    public FakeSseServer SseServer { get; } = new();

    /// <summary>Number of times <see cref="GetTelemetryAsync"/> has been called.</summary>
    public int TelemetryCallCount { get; private set; }

    /// <summary>Path strings recorded on each <see cref="GetTelemetryAsync"/> call.</summary>
    public List<string> TelemetryRequestPaths { get; } = new();

    /// <summary>The <see cref="JobMetrics"/> returned by <see cref="GetTelemetryAsync"/>.</summary>
    public JobMetrics? TelemetryResponse { get; set; }

    /// <summary>Jobs returned by <see cref="GetAllJobsAsync"/>.</summary>
    public List<JobSummary> Jobs { get; } = new();

    // ── Diagnostics stream support ────────────────────────────────────────────

    /// <summary>Channel for controlling the fake diagnostics stream.</summary>
    private Channel<DiagnosticLogRecord> _diagnostics = Channel.CreateUnbounded<DiagnosticLogRecord>();

    /// <summary>Number of times StreamDiagnosticsAsync has been entered.</summary>
    public int DiagnosticsStreamCallCount { get; private set; }

    /// <summary>Push a DiagnosticLogRecord into the fake diagnostics stream.</summary>
    public void PushDiagnosticRecord(DiagnosticLogRecord record)
        => _diagnostics.Writer.TryWrite(record);

    /// <summary>Complete the diagnostics stream cleanly.</summary>
    public void CompleteDiagnosticsStream()
        => _diagnostics.Writer.TryComplete();

    // ── IControlPlaneClient ───────────────────────────────────────────────────

    public Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<JobSummary>>(Jobs);

    public Task<JobMetrics?> GetTelemetryAsync(Guid jobId, CancellationToken ct)
    {
        TelemetryCallCount++;
        TelemetryRequestPaths.Add($"/jobs/{jobId}/telemetry");
        return Task.FromResult(TelemetryResponse);
    }

    public IAsyncEnumerable<ProgressEvent> FollowLogsAsync(
        Guid jobId,
        CancellationToken ct,
        long? lastEventSequence = null)
        => SseServer.GetEventsAsync(jobId, ct);

    public async IAsyncEnumerable<DiagnosticLogRecord> StreamDiagnosticsAsync(
        Guid jobId,
        string? level,
        [EnumeratorCancellation] CancellationToken ct)
    {
        DiagnosticsStreamCallCount++;
        await foreach (var rec in _diagnostics.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return rec;
    }

    public Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<JobBootstrap?>(null);

    public Task<JobTaskList?> GetTasksAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<JobTaskList?>(null);
}
