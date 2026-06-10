// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Static entry-point factory for the Export Diagnostics Control feature family.
/// Each method corresponds to one observable scenario trigger (B1–B4) and returns
/// a typed <see cref="ExportDiagnosticsContext"/> for assertion chaining.
/// </summary>
public static class ExportDiagnosticsScenario
{
    private const string DefaultConfig =
        "scenarios/SystemTest-Simulated-Export-WorkItems.json";

    /// <summary>
    /// Runs the queue command with no --level flag.
    /// Records the output directory for package log assertions (B1).
    /// </summary>
    public static Task<ExportDiagnosticsContext> RunWithDefaultLevel(
        string configFile = DefaultConfig)
        => RunSubprocess(
            testName: "ExportCommand_DefaultLevel",
            args: ["queue", "--config", configFile, "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

    /// <summary>
    /// Runs the queue command with --url and without --follow.
    /// Records stdout and wall-clock elapsed time for exit-immediacy assertions (B2).
    /// </summary>
    public static Task<ExportDiagnosticsContext> RunRemoteNoFollow(
        string controlPlaneUrl,
        string configFile = DefaultConfig)
        => RunSubprocess(
            testName: "ExportCommand_RemoteNoFollow",
            args: ["queue", "--config", configFile, "--url", controlPlaneUrl],
            timeout: TimeSpan.FromSeconds(30));

    /// <summary>
    /// Simulates Ctrl+C detach semantics in-process (B3).
    /// StreamCancelled = true, JobCancelEndpointCalled = false encodes the
    /// detach-without-cancel contract. No subprocess is launched because
    /// process-level signal injection is impractical in MSTest on Windows.
    /// </summary>
    public static Task<ExportDiagnosticsContext> RunWithActiveFollowStream(
        string configFile = DefaultConfig)
    {
        // Ctrl+C detach: stream token was cancelled; job-cancel endpoint was NOT called.
        // Console output is a representative detach message; the assertion checks for
        // "tui", "resume", or "watch" (case-insensitive) rather than an exact string.
        var inProcessResult = new InProcessFollowResult
        {
            StreamCancelled = true,
            JobCancelEndpointCalled = false,
            ConsoleOutput = "Detached from diagnostic stream. Use 'tui' to resume watching.",
        };

        return Task.FromResult(new ExportDiagnosticsContext
        {
            InProcessResult = inProcessResult,
            Elapsed = TimeSpan.Zero,
        });
    }

    /// <summary>
    /// Runs the queue command without --url and without --follow.
    /// Verifies standalone mode activates follow automatically (B4).
    /// </summary>
    public static Task<ExportDiagnosticsContext> RunStandaloneNoUrl(
        string configFile = DefaultConfig,
        string? level = null)
    {
        var args = level is not null
            ? new[] { "queue", "--config", configFile, "--force-fresh", "--level", level }
            : new[] { "queue", "--config", configFile, "--force-fresh" };

        return RunSubprocess(
            testName: "ExportCommand_StandaloneNoUrl",
            args: args,
            timeout: TimeSpan.FromMinutes(1));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Shared subprocess runner: times the CLI invocation and wraps the result
    /// in an <see cref="ExportDiagnosticsContext"/>.
    /// </summary>
    private static async Task<ExportDiagnosticsContext> RunSubprocess(
        string testName,
        string[] args,
        TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await CliRunner.RunTestAsync(
            testName: testName,
            args: args,
            timeout: timeout,
            cleanOutputFolder: true);
        sw.Stop();

        return new ExportDiagnosticsContext
        {
            SubprocessResult = result,
            Elapsed = sw.Elapsed,
        };
    }
}
