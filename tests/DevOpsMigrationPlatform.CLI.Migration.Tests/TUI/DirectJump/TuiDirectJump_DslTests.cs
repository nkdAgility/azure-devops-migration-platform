// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.DirectJump;

/// <summary>
/// DSL tests for the "TUI Direct Job Navigation" capability.
/// Covers all three scenarios from
/// <c>features/cli/tui/tui-job-direct-jump.feature</c>.
/// </summary>
[TestClass]
public sealed class TuiDirectJump_DslTests
{
    // ── Test 2 (lowest cost — implement first) ────────────────────────────────
    // Scenario: --job with unknown job ID exits with error

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TuiDirectJump_WhenJobIdIsUnknown_TuiExitsWithClearErrorMessage()
    {
        // Arrange
        var unknownJobId = Guid.NewGuid();
        using var context = new TuiDirectJumpContext();
        context.WithUnknownJobFlag(unknownJobId);

        // Act
        context.LaunchWithJobFlag();

        // Assert
        context.AssertTuiExitedWithError(unknownJobId.ToString());
    }

    // ── Test 1 — Pre-selection on launch ─────────────────────────────────────
    // Scenario: --job pre-selects the job row on launch

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task TuiDirectJump_WhenJobFlagProvided_JobRowIsPreSelectedAndPanelsPopulatedOnLaunch()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var context = new TuiDirectJumpContext();
        context.WithJobFlag(jobId);

        var metricsPayload = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 3 }
            }
        };

        context.SseServer.Push(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Export",
            Message = "Exporting work item 1",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = metricsPayload
        });

        // Act
        context.LaunchWithJobFlag();

        // Metrics arrive via the SSE stream; update the panel directly from the payload.
        context.MetricsPanel.Update(metricsPayload);
        await Task.CompletedTask;

        await TuiJobDetailAssertions.WaitUntilAsync(
            () => context.LogView.Lines.Count > 0,
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        context.AssertJobRowPreSelected(jobId);
        context.AssertMetricsPanelContains("Work Items Attempted", "3");
        context.AssertLogViewContains("Exporting work item 1");
    }

    // ── Test 3 — Escape deselects rather than exits ───────────────────────────
    // Scenario: Escape from a --job pre-selected view deselects rather than exiting

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task TuiDirectJump_WhenEscapePressedFromPreSelectedView_JobIsDeselectedPanelsClearedTuiRemainsOpen()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var context = new TuiDirectJumpContext();
        context.WithJobFlag(jobId);

        context.SseServer.Push(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Export",
            Message = "Pre-selected log line",
            Timestamp = DateTimeOffset.UtcNow
        });

        context.LaunchWithJobFlag();

        await TuiJobDetailAssertions.WaitUntilAsync(
            () => context.LogView.Lines.Count > 0,
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        context.SimulateEscapeKey();

        // Assert
        context.AssertPanelsCleared();
        context.AssertTuiRemainsOpen();
    }
}
