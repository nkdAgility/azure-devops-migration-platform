// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// Test context builder for TUI job-detail scenarios.
/// Wires <see cref="TuiLogView"/> and <see cref="TelemetryPanel"/> against controlled fakes.
/// Dispose to cancel all outstanding streams.
/// </summary>
public sealed class TuiJobDetailContext : IDisposable
{
    // ── Fakes ────────────────────────────────────────────────────────────────
    /// <summary>Fake control-plane client; exposes <see cref="FakeControlPlaneClient.SseServer"/>.</summary>
    public FakeControlPlaneClient Client { get; } = new();

    /// <summary>Convenience accessor to the embedded SSE server.</summary>
    public FakeSseServer SseServer => Client.SseServer;

    // ── System under test ────────────────────────────────────────────────────
    /// <summary>The TUI log panel under test.</summary>
    public TuiLogView LogView { get; }

    /// <summary>The TUI metrics panel under test.</summary>
    public TelemetryPanel MetricsPanel { get; } = new();

    // ── Status-event capture ─────────────────────────────────────────────────
    private string? _lastJobEndedState;

    // ── Cancellation root ────────────────────────────────────────────────────
    private readonly CancellationTokenSource _rootCts = new();
    public CancellationToken Token => _rootCts.Token;

    public TuiJobDetailContext()
    {
        LogView = new TuiLogView(Client, new DirectUiDispatcher());
        LogView.OnJobEnded += state => _lastJobEndedState = state;
    }

    // ── Builder methods ──────────────────────────────────────────────────────

    /// <summary>Seeds a running job into the client's job list.</summary>
    public TuiJobDetailContext WithRunningJob(Guid jobId)
    {
        Client.Jobs.Add(new JobSummary(jobId, "Migrate", "Running", "test@example.com", DateTimeOffset.UtcNow));
        return this;
    }

    /// <summary>Seeds a terminal-state job (Completed or Failed) into the client's job list.</summary>
    public TuiJobDetailContext WithTerminalJob(Guid jobId, string status)
    {
        Client.Jobs.Add(new JobSummary(jobId, "Migrate", status, "test@example.com", DateTimeOffset.UtcNow));
        return this;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>Binds the log view to the given job (simulates row selection).</summary>
    public void SelectJob(Guid jobId)
    {
        LogView.ClearAndBind(jobId, _rootCts.Token);
    }

    /// <summary>Calls <see cref="TuiLogView.Clear"/> to simulate Escape/deselect.</summary>
    public void DeselectJob()
    {
        LogView.Clear();
    }

    /// <summary>
    /// Simulates the operator pressing Tab within the Log Panel.
    /// Invokes TuiLogView.OnKeyDown with a synthetic Tab key event without
    /// requiring a running Terminal.Gui Application.
    /// </summary>
    public void SimulateTabPress()
    {
        var syntheticKey = new Key(KeyCode.Tab);
        LogView.NewKeyDownEvent(syntheticKey);
    }

    /// <summary>
    /// Pushes a DiagnosticLogRecord into the fake diagnostics stream
    /// so it appears in the log view.
    /// </summary>
    public void PushDiagnosticRecord(DiagnosticLogRecord record)
        => Client.PushDiagnosticRecord(record);

    /// <summary>
    /// Completes the fake diagnostics stream cleanly.
    /// </summary>
    public void CompleteDiagnosticsStream()
        => Client.CompleteDiagnosticsStream();

    // ── Assertions ────────────────────────────────────────────────────────────

    public void AssertMetricsPanelContains(string label, string value)
        => TuiJobDetailAssertions.AssertMetricsPanelContains(MetricsPanel, label, value);

    public void AssertLogViewContains(string text)
        => TuiJobDetailAssertions.AssertLogViewContains(LogView, text);

    public void AssertLogViewDoesNotContain(string text)
        => TuiJobDetailAssertions.AssertLogViewDoesNotContain(LogView, text);

    public void AssertLogViewHasSeparator()
        => TuiJobDetailAssertions.AssertLogViewHasSeparator(LogView);

    public void AssertStatusEventFired(string expectedState)
        => Assert.AreEqual(expectedState, _lastJobEndedState,
            $"Expected OnJobEnded to fire with '{expectedState}' but got '{_lastJobEndedState}'.");

    public void AssertMode(string expectedLabel)
        => TuiJobDetailAssertions.AssertMode(LogView, expectedLabel);

    public void AssertLogViewContainsDiagnosticRecord(string level, string message)
        => TuiJobDetailAssertions.AssertLogViewContainsDiagnosticRecord(LogView, level, message);

    public void AssertDiagnosticsStreamWasCalled()
        => Assert.IsTrue(Client.DiagnosticsStreamCallCount >= 1,
            $"Expected StreamDiagnosticsAsync to have been called at least once " +
            $"but DiagnosticsStreamCallCount={Client.DiagnosticsStreamCallCount}.");

    public void AssertNoReconnectAttempts()
        => Assert.AreEqual(0, SseServer.ReconnectAttemptCount,
            $"Expected no reconnect attempts but SseServer.ReconnectAttemptCount={SseServer.ReconnectAttemptCount}.");

    public void AssertSseSubscriptionsCancelled()
    {
        foreach (var token in SseServer.IssuedTokens)
            Assert.IsTrue(token.IsCancellationRequested,
                "Expected all SSE-issued CancellationTokens to be cancelled after deselect.");
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _rootCts.Cancel();
        _rootCts.Dispose();
        LogView.Dispose();
    }
}
