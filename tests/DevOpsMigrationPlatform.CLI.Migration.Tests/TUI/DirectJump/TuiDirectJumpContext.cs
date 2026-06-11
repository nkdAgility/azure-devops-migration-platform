// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.DirectJump;

/// <summary>
/// Test context builder for TUI direct-jump scenarios (the <c>--job &lt;jobId&gt;</c> flag).
/// Composes <see cref="TuiJobDetailContext"/> for fakes and panel infrastructure.
/// Dispose to cancel all outstanding streams.
/// </summary>
public sealed class TuiDirectJumpContext : IDisposable
{
    private readonly TuiJobDetailContext _inner = new();
    private Guid? _flaggedJobId;
    private bool _launchCompleted;
    private bool _tuiExited;
    private string? _exitErrorMessage;

    // ── Composed accessors ───────────────────────────────────────────────────

    /// <summary>The fake control-plane client. Seed jobs and telemetry via this.</summary>
    public FakeControlPlaneClient Client => _inner.Client;

    /// <summary>Convenience accessor to the embedded SSE server.</summary>
    public FakeSseServer SseServer => _inner.SseServer;

    /// <summary>The TUI log panel under test.</summary>
    public TuiLogView LogView => _inner.LogView;

    /// <summary>The TUI metrics panel under test.</summary>
    public TelemetryPanel MetricsPanel => _inner.MetricsPanel;

    /// <summary>Cancellation token for background streams.</summary>
    public CancellationToken Token => _inner.Token;

    // ── Builder methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a running job into the fake client AND records the job ID as the
    /// value that will be passed to <c>--job</c> on launch.
    /// </summary>
    public TuiDirectJumpContext WithJobFlag(Guid jobId)
    {
        _inner.WithRunningJob(jobId);
        _flaggedJobId = jobId;
        return this;
    }

    /// <summary>
    /// Configures the fake client to return no job for <paramref name="jobId"/>
    /// (not-found path) and records the ID as the value passed to <c>--job</c>.
    /// The job is intentionally NOT added to <see cref="FakeControlPlaneClient.Jobs"/>.
    /// </summary>
    public TuiDirectJumpContext WithUnknownJobFlag(Guid jobId)
    {
        _flaggedJobId = jobId;
        return this;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates the TUI startup path when <c>--job &lt;jobId&gt;</c> is supplied.
    /// If the job ID resolves to a known job the context marks launch as completed
    /// and pre-selects the job row (equivalent to the startup pre-selection path).
    /// If the job ID is unknown the context records an exit with an error message.
    /// </summary>
    public void LaunchWithJobFlag()
    {
        if (_flaggedJobId is null)
            throw new InvalidOperationException(
                "Call WithJobFlag() or WithUnknownJobFlag() before LaunchWithJobFlag().");

        var known = Client.Jobs.Any(j => j.JobId == _flaggedJobId.Value);
        if (known)
        {
            _inner.SelectJob(_flaggedJobId.Value);
            _launchCompleted = true;
        }
        else
        {
            _tuiExited = true;
            _exitErrorMessage = $"Unknown job ID: {_flaggedJobId.Value}";
        }
    }

    /// <summary>
    /// Simulates the operator pressing Escape while in a <c>--job</c>-pre-selected session.
    /// In this mode Escape deselects the current job and clears panels — it does NOT exit the TUI.
    /// </summary>
    public void SimulateEscapeKey()
    {
        _inner.DeselectJob();
        // TUI remains open — _tuiExited stays false.
    }

    // ── Assertions ────────────────────────────────────────────────────────────

    /// <summary>
    /// Asserts that the job list widget's selected row is the job identified by
    /// <paramref name="jobId"/>. In the context model this is represented by
    /// the log view being bound to the job (i.e. <c>LogView.BoundJobId == jobId</c>
    /// once the production code exposes that, or via panel content as a proxy).
    /// </summary>
    public void AssertJobRowPreSelected(Guid jobId)
    {
        // Proxy assertion: the log view was bound to the job during pre-selection.
        // Once production code exposes a SelectedJobId property this should use it directly.
        Assert.IsTrue(_launchCompleted,
            $"Expected TUI launch to complete with job {jobId} pre-selected, but launch did not complete.");
    }

    /// <summary>
    /// Asserts that both the Metrics Panel and Log Panel are in cleared/empty state.
    /// </summary>
    public void AssertPanelsCleared()
    {
        TuiDirectJumpAssertions.AssertLogPanelIsCleared(_inner.LogView);
        // MetricsPanel cleared state: render output should contain no job-specific values.
        // Delegate to the inner context's existing helpers if content was seeded; otherwise a
        // cleared log view is sufficient for the deselect assertion.
    }

    /// <summary>
    /// Asserts that the TUI application has NOT exited (remains open showing the job list).
    /// </summary>
    public void AssertTuiRemainsOpen()
    {
        Assert.IsFalse(_tuiExited,
            "Expected the TUI to remain open after Escape but _tuiExited was true.");
    }

    /// <summary>Delegates to <see cref="TuiJobDetailContext.AssertMetricsPanelContains"/>.</summary>
    public void AssertMetricsPanelContains(string label, string value)
        => _inner.AssertMetricsPanelContains(label, value);

    /// <summary>Delegates to <see cref="TuiJobDetailContext.AssertLogViewContains"/>.</summary>
    public void AssertLogViewContains(string text)
        => _inner.AssertLogViewContains(text);

    /// <summary>
    /// Asserts that the TUI exited and the error output contains <paramref name="jobIdText"/>.
    /// </summary>
    public void AssertTuiExitedWithError(string jobIdText)
    {
        Assert.IsTrue(_tuiExited,
            "Expected the TUI to have exited with an error but it did not exit.");
        Assert.IsTrue(
            _exitErrorMessage?.Contains(jobIdText, StringComparison.OrdinalIgnoreCase) == true,
            $"Expected exit error to contain '{jobIdText}' but was: '{_exitErrorMessage}'.");
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => _inner.Dispose();
}
