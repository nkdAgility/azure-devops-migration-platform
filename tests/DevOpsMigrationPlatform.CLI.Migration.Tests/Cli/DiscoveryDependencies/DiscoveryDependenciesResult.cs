// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryDependencies;

/// <summary>
/// Captures all observable outputs of a <c>discovery dependencies</c> CLI invocation.
/// </summary>
public sealed class DiscoveryDependenciesResult : IAsyncDisposable
{
    internal DiscoveryDependenciesResult(
        int exitCode,
        string standardOutput,
        string standardError,
        bool timedOut,
        string workingDirectory,
        string resolvedCsvPath,
        string defaultCsvPath)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        TimedOut = timedOut;
        WorkingDirectory = workingDirectory;
        ResolvedCsvPath = resolvedCsvPath;
        DefaultCsvPath = defaultCsvPath;
    }

    // ── raw captures ─────────────────────────────────────────────────────────

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public bool TimedOut { get; }
    public string WorkingDirectory { get; }

    /// <summary>Absolute path to the CSV the command was expected to write (default or --output).</summary>
    public string ResolvedCsvPath { get; }

    /// <summary>Absolute path to the default CSV location (working-dir/discovery-dependencies.csv).</summary>
    public string DefaultCsvPath { get; }

    // ── exit-code assertions ──────────────────────────────────────────────────

    public DiscoveryDependenciesResult AssertExitCodeZero()
    {
        Assert.AreEqual(0, ExitCode,
            $"Expected exit code 0. Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    // ── filesystem assertions ─────────────────────────────────────────────────

    /// <summary>Asserts the CSV file exists at <see cref="ResolvedCsvPath"/>.</summary>
    public DiscoveryDependenciesResult AssertCsvFileExists()
    {
        Assert.IsTrue(File.Exists(ResolvedCsvPath),
            $"Expected CSV file to exist at '{ResolvedCsvPath}'.");
        return this;
    }

    /// <summary>Asserts the default CSV path does NOT exist (for --output override scenarios).</summary>
    public DiscoveryDependenciesResult AssertDefaultCsvPathNotUsed()
    {
        Assert.IsFalse(File.Exists(DefaultCsvPath),
            $"Expected the default CSV path '{DefaultCsvPath}' to be absent when --output is set.");
        return this;
    }

    /// <summary>
    /// Asserts the first line of the CSV equals <paramref name="expectedHeader"/> exactly.
    /// </summary>
    public DiscoveryDependenciesResult AssertCsvHeaderEquals(string expectedHeader)
    {
        AssertCsvFileExists();
        var firstLine = File.ReadLines(ResolvedCsvPath).First();
        Assert.AreEqual(expectedHeader, firstLine,
            $"CSV header mismatch in '{ResolvedCsvPath}'.");
        return this;
    }

    /// <summary>
    /// Asserts the CSV contains exactly <paramref name="expectedLineCount"/> lines
    /// (including the header row).
    /// </summary>
    public DiscoveryDependenciesResult AssertCsvLineCount(int expectedLineCount)
    {
        AssertCsvFileExists();
        var lineCount = File.ReadLines(ResolvedCsvPath).Count();
        Assert.AreEqual(expectedLineCount, lineCount,
            $"Expected {expectedLineCount} line(s) in '{ResolvedCsvPath}', found {lineCount}.");
        return this;
    }

    /// <summary>
    /// Asserts the CSV contains more than one line (header + at least one data row).
    /// </summary>
    public DiscoveryDependenciesResult AssertCsvHasDataRows()
    {
        AssertCsvFileExists();
        var lineCount = File.ReadLines(ResolvedCsvPath).Count();
        Assert.IsTrue(lineCount > 1,
            $"Expected at least one data row in '{ResolvedCsvPath}', found only {lineCount} line(s).");
        return this;
    }

    // ── terminal-output assertions ────────────────────────────────────────────

    /// <summary>Asserts stdout contains <paramref name="expectedFragment"/>.</summary>
    public DiscoveryDependenciesResult AssertStdoutContains(string expectedFragment)
    {
        StringAssert.Contains(StandardOutput, expectedFragment,
            $"Expected stdout to contain '{expectedFragment}'.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>Asserts stdout does NOT contain <paramref name="fragment"/>.</summary>
    public DiscoveryDependenciesResult AssertStdoutDoesNotContain(string fragment)
    {
        Assert.IsFalse(StandardOutput.Contains(fragment),
            $"Expected stdout NOT to contain '{fragment}'.\nStdout: {StandardOutput}");
        return this;
    }

    // ── cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(WorkingDirectory))
        {
            try { Directory.Delete(WorkingDirectory, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
        return ValueTask.CompletedTask;
    }
}
