// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// Static assertion helpers for TUI log-view and metrics-panel assertions.
/// </summary>
public static class TuiJobDetailAssertions
{
    /// <summary>
    /// Spins until <paramref name="condition"/> returns <c>true</c> or the
    /// <paramref name="timeout"/> elapses, polling every 50 ms.
    /// Returns without throwing; callers should follow with an assertion.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(50, cts.Token).ConfigureAwait(false);
    }


    /// <summary>
    /// Renders <paramref name="panel"/> to a no-colour <see cref="IAnsiConsole"/> string writer
    /// and asserts the output contains both <paramref name="label"/> and <paramref name="value"/>.
    /// </summary>
    public static void AssertMetricsPanelContains(TelemetryPanel panel, string label, string value)
    {
        var output = new System.Text.StringBuilder();
        var writer = new StringWriter(output);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            ColorSystem = (ColorSystemSupport)ColorSystem.NoColors,
            Ansi = AnsiSupport.No,
        });

        panel.Render(console);

        var text = output.ToString();
        Assert.IsTrue(text.Contains(label),
            $"Metrics panel should contain label '{label}' but rendered:\n{text}");
        Assert.IsTrue(text.Contains(value),
            $"Metrics panel should contain value '{value}' but rendered:\n{text}");
    }

    /// <summary>
    /// Reads the internal <c>Lines</c> accessor of <paramref name="view"/> and asserts
    /// at least one line contains <paramref name="text"/>.
    /// </summary>
    public static void AssertLogViewContains(TuiLogView view, string text)
    {
        var lines = view.Lines;
        Assert.IsTrue(
            lines.Any(l => l.Contains(text, System.StringComparison.OrdinalIgnoreCase)),
            $"Log view should contain '{text}' but lines were:\n{string.Join("\n", lines)}");
    }

    /// <summary>
    /// Asserts the log view does NOT contain <paramref name="text"/>.
    /// </summary>
    public static void AssertLogViewDoesNotContain(TuiLogView view, string text)
    {
        var lines = view.Lines;
        Assert.IsFalse(
            lines.Any(l => l.Contains(text, System.StringComparison.OrdinalIgnoreCase)),
            $"Log view should NOT contain '{text}' but found it in lines:\n{string.Join("\n", lines)}");
    }

    /// <summary>
    /// Asserts the log view contains the separator line written on terminal-state:
    /// either "── Job Completed ──" or "── Job Failed ──".
    /// </summary>
    public static void AssertLogViewHasSeparator(TuiLogView view)
    {
        var lines = view.Lines;
        Assert.IsTrue(
            lines.Any(l => l.Contains("Job Completed") || l.Contains("Job Failed")),
            $"Log view should contain a terminal-state separator but lines were:\n{string.Join("\n", lines)}");
    }
}
