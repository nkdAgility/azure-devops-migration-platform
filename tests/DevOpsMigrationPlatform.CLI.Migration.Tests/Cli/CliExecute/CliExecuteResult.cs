// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.CliExecute;

/// <summary>
/// Captures all observable outputs of a CLI command execution safety invocation.
/// Implements <see cref="IAsyncDisposable"/> so callers can use <c>await using</c>.
/// </summary>
public sealed class CliExecuteResult : IAsyncDisposable
{
    internal CliExecuteResult(
        int exitCode,
        string standardOutput,
        string standardError,
        bool timedOut)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        TimedOut = timedOut;
    }

    // ── raw captures ─────────────────────────────────────────────────────────

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public bool TimedOut { get; }

    // ── assertion extensions ──────────────────────────────────────────────────

    /// <summary>Asserts exit code is not 0.</summary>
    public CliExecuteResult AssertExitCodeNonZero()
    {
        Assert.AreNotEqual(0, ExitCode,
            $"Expected non-zero exit code. Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>Asserts exit code is 0.</summary>
    public CliExecuteResult AssertExitCodeZero()
    {
        Assert.AreEqual(0, ExitCode,
            $"Expected exit code 0 (success). Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts <see cref="StandardError"/> contains <paramref name="expectedFragment"/>.
    /// Replaces the vacuous "display a clear error message" feature assertion.
    /// </summary>
    public CliExecuteResult AssertStderrContains(string expectedFragment)
    {
        StringAssert.Contains(StandardError, expectedFragment,
            $"Expected stderr to contain '{expectedFragment}'.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts either stdout or stderr contains <paramref name="expectedFragment"/>.
    /// </summary>
    public CliExecuteResult AssertOutputContains(string expectedFragment)
    {
        var combined = StandardOutput + StandardError;
        Assert.IsTrue(combined.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
            $"Expected combined output to contain '{expectedFragment}'.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts <see cref="StandardOutput"/> contains <paramref name="expectedFragment"/>.
    /// Replaces the vacuous "display comprehensive help text" feature assertion.
    /// </summary>
    public CliExecuteResult AssertStdoutContains(string expectedFragment)
    {
        StringAssert.Contains(StandardOutput, expectedFragment,
            $"Expected stdout to contain '{expectedFragment}'.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts neither stdout nor stderr contains stack-trace markers
    /// (<c>"Unhandled exception"</c>, <c>"   at "</c>).
    /// Replaces the vacuous "no unhandled exceptions should occur" feature assertion.
    /// </summary>
    public CliExecuteResult AssertNoUnhandledException()
    {
        var combined = StandardOutput + StandardError;
        Assert.IsFalse(combined.Contains("Unhandled exception"),
            $"Output contains 'Unhandled exception' marker.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        Assert.IsFalse(combined.Contains("   at "),
            $"Output contains stack-trace marker '   at '.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts the combined output contains <c>"--help"</c> or <c>"help"</c>.
    /// Replaces the vacuous "help information should be suggested" feature assertion.
    /// </summary>
    public CliExecuteResult AssertHelpSuggested()
    {
        var combined = StandardOutput + StandardError;
        Assert.IsTrue(
            combined.Contains("--help") || combined.Contains("help"),
            $"Expected output to suggest help (contain '--help' or 'help').\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts <see cref="StandardError"/> is empty (no errors displayed).
    /// </summary>
    public CliExecuteResult AssertStderrEmpty()
    {
        Assert.AreEqual(string.Empty, StandardError.Trim(),
            $"Expected empty stderr. Actual:\n{StandardError}");
        return this;
    }

    // ── cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
