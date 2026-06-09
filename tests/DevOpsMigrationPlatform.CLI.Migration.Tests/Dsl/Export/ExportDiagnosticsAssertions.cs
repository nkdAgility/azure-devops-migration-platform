// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Fluent assertion extensions for <see cref="ExportDiagnosticsContext"/> and related types.
/// All methods call <c>Assert.*</c> directly and return <c>this</c> for chaining.
/// </summary>
public static class ExportDiagnosticsAssertions
{
    // ── Log level assertions ──────────────────────────────────────────────────

    /// <summary>
    /// Asserts that every record in <paramref name="records"/> has a Level at or above
    /// <paramref name="minimumLevel"/> (ordinal comparison via <see cref="LogLevelOrder"/>).
    /// </summary>
    public static IReadOnlyList<NdjsonLogRecord> ShouldContainOnlyLevelAndAbove(
        this IReadOnlyList<NdjsonLogRecord> records,
        string minimumLevel)
    {
        var minRank = LogLevelOrder.Rank(minimumLevel);
        Assert.IsTrue(minRank >= 0,
            $"Unknown minimum level '{minimumLevel}'.");

        foreach (var record in records)
        {
            var rank = LogLevelOrder.Rank(record.Level);
            Assert.IsTrue(rank >= minRank,
                $"Log record with level '{record.Level}' is below the minimum level '{minimumLevel}'. " +
                $"Message: {record.Message}");
        }

        return records;
    }

    // ── Remote no-follow assertions ───────────────────────────────────────────

    private static readonly Regex _uuidPattern =
        new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

    /// <summary>
    /// Asserts stdout contains a UUID-shaped job ID.
    /// </summary>
    public static ExportDiagnosticsContext ShouldPrintJobId(
        this ExportDiagnosticsContext ctx)
    {
        var output = ctx.SubprocessResult?.StandardOutput ?? string.Empty;
        Assert.IsTrue(_uuidPattern.IsMatch(output),
            $"Expected a UUID-shaped job ID in stdout. Got:\n{output}");
        return ctx;
    }

    /// <summary>
    /// Asserts the process exited within <paramref name="maxElapsed"/>.
    /// Default: 10 seconds (a follow loop would take much longer).
    /// </summary>
    public static ExportDiagnosticsContext ShouldHaveExitedImmediately(
        this ExportDiagnosticsContext ctx,
        TimeSpan? maxElapsed = null)
    {
        var threshold = maxElapsed ?? TimeSpan.FromSeconds(10);
        Assert.IsTrue(ctx.Elapsed <= threshold,
            $"Expected CLI to exit within {threshold.TotalSeconds:F1}s but elapsed was " +
            $"{ctx.Elapsed.TotalSeconds:F1}s. This suggests the follow loop was entered.");
        return ctx;
    }

    // ── Follow-detach assertions ──────────────────────────────────────────────

    /// <summary>
    /// Asserts the stream was cancelled and the job-cancel endpoint was NOT called.
    /// </summary>
    public static ExportDiagnosticsContext ShouldHaveDetachedWithoutCancellingJob(
        this ExportDiagnosticsContext ctx)
    {
        Assert.IsNotNull(ctx.InProcessResult,
            "InProcessResult must be set for follow-detach assertions.");
        Assert.IsTrue(ctx.InProcessResult!.StreamCancelled,
            "Expected the follow-stream CancellationToken to have been cancelled.");
        Assert.IsFalse(ctx.InProcessResult.JobCancelEndpointCalled,
            "Expected the job-cancel endpoint NOT to have been called (detach-without-cancel semantics).");
        return ctx;
    }

    /// <summary>
    /// Asserts stdout contains a hint to resume watching with the TUI.
    /// </summary>
    public static ExportDiagnosticsContext ShouldPrintTuiResumeHint(
        this ExportDiagnosticsContext ctx)
    {
        var output = ctx.InProcessResult?.ConsoleOutput ?? string.Empty;
        Assert.IsTrue(
            output.Contains("tui", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("resume", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("watch", StringComparison.OrdinalIgnoreCase),
            $"Expected a TUI resume hint in console output. Got:\n{output}");
        return ctx;
    }

    // ── Standalone implied-follow assertions ──────────────────────────────────

    /// <summary>
    /// Asserts that diagnostic lines appeared in stdout during the run,
    /// confirming follow mode was activated implicitly.
    /// </summary>
    public static ExportDiagnosticsContext ShouldHaveStreamedDiagnosticsToConsole(
        this ExportDiagnosticsContext ctx)
    {
        var output = (ctx.SubprocessResult?.StandardOutput ?? string.Empty) +
                     (ctx.SubprocessResult?.StandardError ?? string.Empty);
        Assert.IsFalse(string.IsNullOrWhiteSpace(output),
            "Expected diagnostic output to have been streamed to console but nothing was captured.");
        return ctx;
    }

    /// <summary>
    /// Asserts the process exited 0 (standalone run completed successfully).
    /// </summary>
    public static ExportDiagnosticsContext ShouldHaveExitedZero(
        this ExportDiagnosticsContext ctx)
    {
        Assert.IsNotNull(ctx.SubprocessResult,
            "SubprocessResult must be set for exit-code assertions.");
        Assert.IsFalse(ctx.SubprocessResult!.TimedOut,
            "CLI timed out — the export may be hung.");
        Assert.AreEqual(0, ctx.SubprocessResult.ExitCode,
            $"Expected exit code 0 but got {ctx.SubprocessResult.ExitCode}. " +
            $"Stdout:\n{ctx.SubprocessResult.StandardOutput}\n" +
            $"Stderr:\n{ctx.SubprocessResult.StandardError}");
        return ctx;
    }
}
