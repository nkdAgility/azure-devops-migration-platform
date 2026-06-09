// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// DSL tests for the "Job selection — panel population" capability.
/// Covers feature scenarios 1 and 6 from
/// <c>features/cli/tui/tui-job-detail.feature</c>.
/// </summary>
[TestClass]
public sealed class TuiJobDetail_PanelPopulation_DslTests
{
    // ── Scenario 1: Selecting a job populates Metrics and Log panels ──────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task TuiJobDetail_WhenJobSelected_MetricsPanelAndLogPanelArePopulated()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var context = new TuiJobDetailContext();
        context.WithRunningJob(jobId);

        // Seed telemetry so the metrics panel has data after the first poll.
        context.Client.TelemetryResponse = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 7 }
            }
        };

        // Seed one ProgressEvent so the log view receives it.
        var evt = new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Export",
            Message = "Exporting work item 1",
            Timestamp = DateTimeOffset.UtcNow
        };
        context.SseServer.Push(evt);

        // Act — select the job; give the background stream loop a moment to process.
        context.SelectJob(jobId);

        // Manually update the metrics panel (simulates the poller firing immediately).
        var metrics = await context.Client.GetTelemetryAsync(jobId, context.Token);
        context.MetricsPanel.Update(metrics);

        // Allow the background stream loop to process the pushed event.
        await TuiJobDetailAssertions.WaitUntilAsync(
            () => context.LogView.Lines.Count > 0,
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        context.AssertMetricsPanelContains("Work Items Attempted", "7");
        context.AssertLogViewContains("Exporting work item 1");
    }

    // ── Scenario 6: Viewing a completed job shows terminal state marker ────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task TuiJobDetail_WhenJobIsInTerminalState_LogViewShowsFinalSeparatorAndStatusEventFired()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var context = new TuiJobDetailContext();
        context.WithTerminalJob(jobId, "Completed");

        // Push one event so that StreamTraceAsync emits the separator on stream end.
        context.SseServer.Push(new ProgressEvent
        {
            Module = "WorkItems",
            Stage = "Complete",
            Message = "All work items migrated.",
            Timestamp = DateTimeOffset.UtcNow
        });

        // Act — select the job, then immediately complete the stream (terminal state).
        context.SelectJob(jobId);

        // Allow the stream loop to receive the first event before we close it.
        await TuiJobDetailAssertions.WaitUntilAsync(
            () => context.LogView.Lines.Count > 0,
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Complete the SSE stream — this causes StreamTraceAsync to append the separator.
        context.SseServer.CompleteStream();

        // Wait for the separator to appear in the log view.
        await TuiJobDetailAssertions.WaitUntilAsync(
            () => context.LogView.Lines.Any(l => l.Contains("Job Completed") || l.Contains("Job Failed")),
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        context.AssertLogViewHasSeparator();
        context.AssertStatusEventFired("Completed");
        context.AssertNoReconnectAttempts();
    }
}
