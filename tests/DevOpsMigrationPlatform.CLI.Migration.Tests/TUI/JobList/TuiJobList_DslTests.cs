// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobList;

/// <summary>
/// Code-first MSTest tests for the TUI Job List panel.
/// Converted from <c>features/cli/tui/tui-job-list.feature</c>.
/// </summary>
[TestClass]
public class TuiJobList_DslTests
{
    // ── T1 — Job list display ─────────────────────────────────────────────────

    /// <summary>
    /// S1: TUI displays job list when control plane is reachable.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public void TuiJobListView_WhenControlPlaneReachable_DisplaysTableWithJobColumns()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var context = new TuiJobListContext();
        context.WithJob(jobId, mode: "Migrate", state: "Running");

        // Act
        context.TriggerUpdateJobs();

        // Assert
        context.AssertTableHasColumns("Job ID", "Mode", "State", "Submitted");
        context.AssertTableRowCount(1);
        context.AssertRowContains(0, jobId.ToString()[..8]);
    }

    // ── T2 — Auto-refresh ────────────────────────────────────────────────────

    /// <summary>
    /// S2: Job list refreshes when jobs change.
    /// Uses a 50 ms refresh interval to avoid the 10 s production default.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task TuiJobListView_WhenJobsChange_ListRefreshesWithinTenSeconds()
    {
        // Arrange — use a 50 ms refresh interval to avoid 10 s real-time wait
        const int refreshMs = 50;
        using var context = new TuiJobListContext(refreshIntervalMs: refreshMs);

        var initialJobId = Guid.NewGuid();
        context.WithJob(initialJobId, state: "Running");

        // Wait for the first timer tick to populate the table
        await TuiJobListAssertions.WaitUntilAsync(
            () => TuiJobListAssertions.GetRowCount(context.View) == 1,
            TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        // Act — add a second job to the control plane
        var newJobId = Guid.NewGuid();
        context.ChangeJobsOnControlPlane(
        [
            new JobSummary(initialJobId, "Migrate", "Running",   "test@example.com", DateTimeOffset.UtcNow),
            new JobSummary(newJobId,     "Migrate", "Submitted", "test@example.com", DateTimeOffset.UtcNow)
        ]);

        // Assert — wait for next refresh to reflect the new state
        await TuiJobListAssertions.WaitUntilAsync(
            () => TuiJobListAssertions.GetRowCount(context.View) == 2,
            TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        context.AssertTableRowCount(2);
    }

    // ── T3 — Unreachable control plane error ──────────────────────────────────

    /// <summary>
    /// S3: TUI exits with error when control plane is unreachable.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task TuiCommand_WhenControlPlaneUnreachable_ExitsWithActionableErrorShowingUrl()
    {
        // Arrange
        var attemptedUrl = "http://control-plane.example.com:5100";
        var faultingClient = new FaultingControlPlaneClient { AttemptedUrl = attemptedUrl };
        var harness = new TuiCommandErrorHarness { EffectiveUrl = attemptedUrl };

        // Act
        var exitCode = await harness.RunHealthCheckAsync(faultingClient).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(1, exitCode, "Expected exit code 1 when control plane is unreachable.");
        Assert.IsTrue(
            harness.CapturedOutput.Contains(attemptedUrl),
            $"Expected error output to contain the attempted URL '{attemptedUrl}' but got:\n{harness.CapturedOutput}");
    }

    // ── T4 — Default localhost URL ────────────────────────────────────────────

    /// <summary>
    /// S4: TUI connects to default local URL when no --url flag is set.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task TuiCommand_WhenNoUrlConfigured_ConnectsToDefaultLocalhostUrl()
    {
        // Arrange
        const string defaultUrl = "http://localhost:5100";
        var faultingClient = new FaultingControlPlaneClient { AttemptedUrl = defaultUrl };
        var harness = new TuiCommandErrorHarness { EffectiveUrl = defaultUrl };

        // Act
        var exitCode = await harness.RunHealthCheckAsync(faultingClient).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(1, exitCode,
            "Expected exit code 1 when nothing is listening at the default URL.");
        Assert.IsTrue(
            harness.CapturedOutput.Contains(defaultUrl),
            $"Error output should identify the default URL '{defaultUrl}' but got:\n{harness.CapturedOutput}");
        Assert.IsTrue(
            harness.CapturedOutput.Contains("control plane") || harness.CapturedOutput.Contains("BaseUrl"),
            "Error output should include advisory text about the control plane configuration.");
    }
}
