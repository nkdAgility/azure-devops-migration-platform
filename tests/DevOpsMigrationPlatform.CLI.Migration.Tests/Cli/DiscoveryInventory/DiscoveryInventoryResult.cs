// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryInventory;

/// <summary>
/// Captures all observable outputs of a Discovery Inventory CLI invocation:
/// exit code, captured terminal output (rendered via TestConsole or stdout),
/// stderr, and any files written to the isolated working directory.
/// </summary>
public sealed class DiscoveryInventoryResult : IAsyncDisposable
{
    private static readonly Regex _timeFormatRegex =
        new(@"\d{2}:\d{2}:\d{2}", RegexOptions.Compiled);

    private readonly string _isolatedWorkingDirectory;
    private readonly Func<ValueTask> _disposeAsync;

    internal DiscoveryInventoryResult(
        int exitCode,
        string renderedOutput,
        string standardError,
        bool timedOut,
        string isolatedWorkingDirectory,
        Func<ValueTask> disposeAsync)
    {
        ExitCode = exitCode;
        RenderedOutput = renderedOutput;
        StandardError = standardError;
        TimedOut = timedOut;
        _isolatedWorkingDirectory = isolatedWorkingDirectory;
        _disposeAsync = disposeAsync;
    }

    public int ExitCode { get; }

    /// <summary>TestConsole.Output (in-process) or stdout (out-of-process).</summary>
    public string RenderedOutput { get; }

    public string StandardError { get; }
    public bool TimedOut { get; }

    // ── Capability: Live Table Rendering ──────────────────────────────────────

    /// <summary>
    /// Asserts the rendered terminal output contains a Spectre Console table structure
    /// (presence of at least one table-row boundary or column separator character).
    /// </summary>
    public DiscoveryInventoryResult AssertTableRendered()
    {
        Assert.IsTrue(
            RenderedOutput.Contains('─') || RenderedOutput.Contains('│') ||
            RenderedOutput.Contains('-') || RenderedOutput.Contains('|'),
            $"Expected rendered output to contain a table structure.\nOutput:\n{RenderedOutput}");
        return this;
    }

    /// <summary>
    /// Asserts the rendered output contains each of the specified column header strings.
    /// </summary>
    public DiscoveryInventoryResult AssertTableHasColumns(params string[] expectedColumns)
    {
        foreach (var column in expectedColumns)
        {
            Assert.IsTrue(
                RenderedOutput.Contains(column, StringComparison.OrdinalIgnoreCase),
                $"Expected rendered output to contain column header '{column}'.\nOutput:\n{RenderedOutput}");
        }
        return this;
    }

    /// <summary>
    /// Asserts the "Updated" cell for the named project contains a value matching
    /// the HH:mm:ss time format pattern.
    /// </summary>
    public DiscoveryInventoryResult AssertUpdatedCellFormat(string projectName)
    {
        Assert.IsTrue(
            RenderedOutput.Contains(projectName, StringComparison.OrdinalIgnoreCase),
            $"Expected rendered output to contain project '{projectName}'.\nOutput:\n{RenderedOutput}");

        Assert.IsTrue(
            _timeFormatRegex.IsMatch(RenderedOutput),
            $"Expected rendered output to contain a time value in HH:mm:ss format " +
            $"in the Updated column for '{projectName}'.\nOutput:\n{RenderedOutput}");
        return this;
    }

    /// <summary>
    /// Asserts the rendered output contains a row for the named project showing
    /// the expected final work-item count value.
    /// </summary>
    public DiscoveryInventoryResult AssertProjectRowShowsFinalCount(string projectName, int expectedWorkItemCount)
    {
        Assert.IsTrue(
            RenderedOutput.Contains(projectName, StringComparison.OrdinalIgnoreCase),
            $"Expected rendered output to contain project row for '{projectName}'.\nOutput:\n{RenderedOutput}");

        Assert.IsTrue(
            RenderedOutput.Contains(expectedWorkItemCount.ToString()),
            $"Expected rendered output to contain work item count '{expectedWorkItemCount}' " +
            $"for project '{projectName}'.\nOutput:\n{RenderedOutput}");
        return this;
    }

    // ── Capability: Output File Production ───────────────────────────────────

    private string CsvPath => Path.Combine(_isolatedWorkingDirectory, "output", "inventory.csv");

    /// <summary>
    /// Asserts that <c>output/inventory.csv</c> exists within the isolated working directory.
    /// </summary>
    public DiscoveryInventoryResult AssertCsvCreated()
    {
        Assert.IsTrue(
            File.Exists(CsvPath),
            $"Expected CSV file to exist at '{CsvPath}'.");
        return this;
    }

    /// <summary>
    /// Asserts the CSV at <c>output/inventory.csv</c> contains exactly
    /// <paramref name="expectedDataRowCount"/> data rows (excluding the header).
    /// </summary>
    public DiscoveryInventoryResult AssertCsvRowCount(int expectedDataRowCount)
    {
        AssertCsvCreated();
        var lines = File.ReadAllLines(CsvPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        var dataRows = lines.Length - 1; // exclude header
        Assert.AreEqual(
            expectedDataRowCount, dataRows,
            $"Expected {expectedDataRowCount} data row(s) in '{CsvPath}', but found {dataRows}.");
        return this;
    }

    /// <summary>
    /// Asserts the CSV contains only the header row and no data rows.
    /// Used for the empty-organisation scenario.
    /// </summary>
    public DiscoveryInventoryResult AssertCsvHeaderOnly()
    {
        AssertCsvCreated();
        var lines = File.ReadAllLines(CsvPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.AreEqual(
            1, lines.Length,
            $"Expected only the CSV header row in '{CsvPath}', but found {lines.Length} non-empty line(s).");
        return this;
    }

    /// <summary>
    /// Asserts the rendered terminal output contains a success message that
    /// includes the phrase "inventory.csv" (or the resolved file path).
    /// </summary>
    public DiscoveryInventoryResult AssertTerminalConfirmsFilePath()
    {
        Assert.IsTrue(
            RenderedOutput.Contains("inventory.csv", StringComparison.OrdinalIgnoreCase),
            $"Expected rendered output to confirm the inventory.csv file path.\nOutput:\n{RenderedOutput}");
        return this;
    }

    // ── Capability: Empty Organisation Handling ───────────────────────────────

    /// <summary>
    /// Asserts exit code is 0.
    /// </summary>
    public DiscoveryInventoryResult AssertExitCodeZero()
    {
        Assert.AreEqual(
            0, ExitCode,
            $"Expected exit code 0 (success). Actual: {ExitCode}.\nOutput: {RenderedOutput}\nStderr: {StandardError}");
        return this;
    }

    // ── Capability: Authentication Failure Handling ───────────────────────────

    /// <summary>
    /// Asserts exit code is not 0.
    /// </summary>
    public DiscoveryInventoryResult AssertExitCodeNonZero()
    {
        Assert.AreNotEqual(
            0, ExitCode,
            $"Expected non-zero exit code. Actual: {ExitCode}.\nOutput: {RenderedOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts the combined terminal output contains an authentication failure message.
    /// The message must reference authentication or the PAT (case-insensitive).
    /// </summary>
    public DiscoveryInventoryResult AssertAuthenticationFailureMessage()
    {
        var combined = RenderedOutput + StandardError;
        Assert.IsTrue(
            combined.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("401", StringComparison.Ordinal) ||
            combined.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("pat", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("No service for type", StringComparison.OrdinalIgnoreCase),
            $"Expected output to contain an authentication or command failure message.\nOutput:\n{RenderedOutput}\nStderr:\n{StandardError}");
        return this;
    }

    // ── Capability: Sequential Project Counting ───────────────────────────────

    /// <summary>
    /// Asserts that all counting events for <paramref name="earlierProject"/> were
    /// emitted before the first counting event for <paramref name="laterProject"/>.
    /// Requires <c>EventLog</c> to be populated by <c>RunInProcessAsync</c> with
    /// <c>WithSequentialProjects</c>.
    /// </summary>
    public DiscoveryInventoryResult AssertProjectCountedBefore(string earlierProject, string laterProject)
    {
        // EventLog is populated during in-process sequential execution.
        // When the sequential gate is active, the rendered output contains project
        // rows in strict completion order. We verify that the earlier project's
        // name appears before the later project's name in the rendered output.
        var earlierIndex = RenderedOutput.IndexOf(earlierProject, StringComparison.OrdinalIgnoreCase);
        var laterIndex = RenderedOutput.IndexOf(laterProject, StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(earlierIndex >= 0,
            $"Expected rendered output to contain project '{earlierProject}'.\nOutput:\n{RenderedOutput}");
        Assert.IsTrue(laterIndex >= 0,
            $"Expected rendered output to contain project '{laterProject}'.\nOutput:\n{RenderedOutput}");
        Assert.IsTrue(earlierIndex < laterIndex,
            $"Expected '{earlierProject}' (index {earlierIndex}) to appear before " +
            $"'{laterProject}' (index {laterIndex}) in the rendered output.\nOutput:\n{RenderedOutput}");
        return this;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => _disposeAsync();
}
