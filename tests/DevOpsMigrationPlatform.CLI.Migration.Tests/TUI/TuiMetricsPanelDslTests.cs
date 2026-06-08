// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

/// <summary>
/// DSL-style tests for the TUI Live Metrics Panel and TelemetryPoller.
/// Migrated from features/platform/telemetry/tui-metrics-panel.feature (scenarios 4–6).
/// </summary>
[TestClass]
public sealed class TuiMetricsPanelDslTests
{
    // ── Scenario: TUI metrics panel shows a waiting message when no snapshot is available ──

    [TestCategory("UnitTest")]
    [TestMethod]
    public void TelemetryPanel_WhenNoMetricsAvailable_BuildContentReturnsWaitingMessage()
    {
        // Arrange – panel with no metrics pushed
        var panel = new TelemetryPanel();

        // Capture rendered text via a TestAnsiConsole string writer
        var output = new System.Text.StringBuilder();
        var writer = new System.IO.StringWriter(output);
        var console = Spectre.Console.AnsiConsole.Create(
            new Spectre.Console.AnsiConsoleSettings
            {
                Out = new Spectre.Console.AnsiConsoleOutput(writer),
                ColorSystem = (Spectre.Console.ColorSystemSupport)Spectre.Console.ColorSystem.NoColors,
                Ansi = Spectre.Console.AnsiSupport.No,
            });

        // Act – render with no metrics (null)
        panel.Render(console);

        // Assert – waiting message is displayed
        var text = output.ToString();
        StringAssert.Contains(text, "(waiting for agent", $"Panel should display waiting message but got:\n{text}");
    }

    // ── Scenario: TUI metrics panel displays snapshot values when a snapshot is received ──

    [TestCategory("UnitTest")]
    [TestMethod]
    public void TelemetryPanel_WhenMetricsPushed_DisplaysWorkItemsAttempted()
    {
        // Arrange – a MetricSnapshot with WorkItemsAttempted = 42 has been received
        var panel = new TelemetryPanel();
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 42 }
            }
        };
        panel.Update(metrics);

        var output = new System.Text.StringBuilder();
        var writer = new System.IO.StringWriter(output);
        var console = Spectre.Console.AnsiConsole.Create(
            new Spectre.Console.AnsiConsoleSettings
            {
                Out = new Spectre.Console.AnsiConsoleOutput(writer),
                ColorSystem = (Spectre.Console.ColorSystemSupport)Spectre.Console.ColorSystem.NoColors,
                Ansi = Spectre.Console.AnsiSupport.No,
            });

        // Act – render with metrics
        panel.Render(console);

        // Assert – "Work Items Attempted" label with value 42 is shown
        var text = output.ToString();
        StringAssert.Contains(text, "Work Items Attempted",
            $"Panel should display 'Work Items Attempted' label but got:\n{text}");
        StringAssert.Contains(text, "42",
            $"Panel should display the value 42 but got:\n{text}");
    }

    // ── Scenario: TUI metrics panel refreshes on each polling interval ────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task TelemetryPoller_WhenIntervalElapses_PollsAgainAndUpdatesPanel()
    {
        // Arrange – a fake HTTP handler that returns 200 with JobMetrics on each call
        var callCount = 0;
        var jobId = Guid.NewGuid();
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 7 }
            }
        };

        var handler = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            var json = JsonSerializer.Serialize(metrics);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var panel = new TelemetryPanel();
        var poller = new TelemetryPoller(httpClient, panel, NullLogger<TelemetryPoller>.Instance);

        using var cts = new CancellationTokenSource();

        // Act – run for just long enough to get 2 polls (immediate + after 1-second interval)
        var task = poller.RunAsync(jobId, intervalSeconds: 1, cts.Token);
        await Task.Delay(1500); // allow at least 2 polls
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // Assert – polled at least twice (once immediately + once after interval)
        Assert.IsTrue(callCount >= 2,
            $"Expected at least 2 HTTP calls but got {callCount}. The poller should re-poll after the interval.");
    }

    // ── Helper ────────────────────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_handler(request));
        }
    }
}
