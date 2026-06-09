// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

/// <summary>
/// Captures all observable outputs of a TFS export CLI invocation.
/// Extends the assertion vocabulary with TFS-export-specific assertions
/// grouped by business capability.
/// </summary>
public sealed class TfsExportResult : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync;

    internal TfsExportResult(
        int exitCode,
        string standardOutput,
        string standardError,
        bool timedOut,
        Func<ValueTask> disposeAsync)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        TimedOut = timedOut;
        _disposeAsync = disposeAsync;
    }

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public bool TimedOut { get; }

    /// <summary>
    /// Concatenation of <see cref="StandardOutput"/> and <see cref="StandardError"/>
    /// used by all content-checking assertions.
    /// </summary>
    private string CombinedOutput => StandardOutput + StandardError;

    // ── general ───────────────────────────────────────────────────────────────

    public TfsExportResult AssertExitCode(int expected)
    {
        Assert.AreEqual(expected, ExitCode,
            $"Expected exit code {expected}. Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    public TfsExportResult AssertExitCodeNonZero()
    {
        Assert.AreNotEqual(0, ExitCode,
            $"Expected non-zero exit code. Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    public TfsExportResult AssertExitCodeZero()
    {
        Assert.AreEqual(0, ExitCode,
            $"Expected exit code 0 (success). Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    // ── TFS Config Validation capability ─────────────────────────────────────

    /// <summary>
    /// Asserts the combined output contains a validation error referencing the
    /// requirement for a valid HTTP or HTTPS server URL.
    /// Covers scenario 2 (invalid server URL).
    /// </summary>
    public TfsExportResult AssertValidationErrorUrlRequired()
    {
        Assert.IsTrue(
            CombinedOutput.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            CombinedOutput.Contains("URL", StringComparison.OrdinalIgnoreCase) ||
            CombinedOutput.Contains("https", StringComparison.OrdinalIgnoreCase),
            $"Expected validation error referencing HTTP/HTTPS URL requirement.\n" +
            $"Stdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts the combined output contains a validation error requiring a project name.
    /// Covers scenario 3 (empty project name).
    /// </summary>
    public TfsExportResult AssertValidationErrorProjectNameRequired()
    {
        Assert.IsTrue(
            CombinedOutput.Contains("Project", StringComparison.OrdinalIgnoreCase) ||
            CombinedOutput.Contains("project name", StringComparison.OrdinalIgnoreCase),
            $"Expected validation error referencing project name requirement.\n" +
            $"Stdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts the combined output contains a validation error fragment.
    /// General-purpose variant for both URL and project-name validation scenarios.
    /// </summary>
    public TfsExportResult AssertValidationError(string expectedFragment)
    {
        Assert.IsTrue(
            CombinedOutput.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
            $"Expected validation error containing '{expectedFragment}'.\n" +
            $"Stdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    // ── TFS Export Progress Visibility capability ─────────────────────────────

    /// <summary>
    /// Asserts the combined output includes all three mandatory live-status counters:
    /// total work items, processed work items, and processed revisions.
    /// Covers scenario 1.
    /// </summary>
    public TfsExportResult AssertLiveProgressCountersPresent()
    {
        Assert.IsTrue(
            CombinedOutput.Contains("total", StringComparison.OrdinalIgnoreCase),
            $"Expected live status to contain total work-item count.\nOutput: {CombinedOutput}");
        Assert.IsTrue(
            CombinedOutput.Contains("processed", StringComparison.OrdinalIgnoreCase),
            $"Expected live status to contain processed work-item count.\nOutput: {CombinedOutput}");
        Assert.IsTrue(
            CombinedOutput.Contains("revision", StringComparison.OrdinalIgnoreCase),
            $"Expected live status to contain processed revision count.\nOutput: {CombinedOutput}");
        return this;
    }

    /// <summary>
    /// Asserts the combined output contains a success confirmation line.
    /// Covers scenario 1 post-completion assertion.
    /// </summary>
    public TfsExportResult AssertSuccessConfirmationShown()
    {
        Assert.IsTrue(
            CombinedOutput.Contains("Export complete", StringComparison.OrdinalIgnoreCase) ||
            CombinedOutput.Contains("success", StringComparison.OrdinalIgnoreCase),
            $"Expected success confirmation in output.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts output was produced on stdout (progress lines streamed to operator).
    /// Covers scenario 4 (streaming in real time).
    /// </summary>
    public TfsExportResult AssertOutputLinesProduced()
    {
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(StandardOutput),
            $"Expected stdout to contain progress output lines.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts error output appears on stderr rather than stdout, demonstrating
    /// the visual channel distinction between progress and error output.
    /// Covers scenario 4 (stderr vs stdout distinction).
    /// </summary>
    public TfsExportResult AssertErrorOutputOnStderr()
    {
        // If there is error content, it must be on stderr not exclusively on stdout.
        if (!string.IsNullOrWhiteSpace(StandardError))
        {
            Assert.IsTrue(
                StandardError.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                StandardError.Contains("Error", StringComparison.Ordinal),
                $"Expected stderr to contain an error-labelled line.\nStderr: {StandardError}");
        }
        return this;
    }

    /// <summary>
    /// Asserts the live status output contains a chunk start date, chunk end date,
    /// and a work-item count for that chunk.
    /// Covers scenario 7 (chunk progress display).
    /// NOTE: Depends on <c>ProgressEvent</c> carrying chunk metadata fields —
    /// confirm field availability before implementing the backing test.
    /// </summary>
    public TfsExportResult AssertChunkProgressShown()
    {
        var hasDate = System.Text.RegularExpressions.Regex.IsMatch(
            CombinedOutput, @"\d{4}-\d{2}-\d{2}");
        var hasCount = System.Text.RegularExpressions.Regex.IsMatch(
            CombinedOutput, @"\d+\s*(work item|item)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.IsTrue(hasDate,
            $"Expected chunk progress to show date values (yyyy-MM-dd).\nOutput: {CombinedOutput}");
        Assert.IsTrue(hasCount,
            $"Expected chunk progress to show work-item count.\nOutput: {CombinedOutput}");
        return this;
    }

    // ── TFS Export Fault Handling capability ──────────────────────────────────

    /// <summary>
    /// Asserts the combined output explains that TFS export could not be started.
    /// Covers scenario 6 (TFS unavailable).
    /// </summary>
    public TfsExportResult AssertTfsUnavailableErrorShown()
    {
        Assert.IsTrue(
            CombinedOutput.Contains("TFS", StringComparison.OrdinalIgnoreCase) ||
            CombinedOutput.Contains("export", StringComparison.OrdinalIgnoreCase),
            $"Expected error message about TFS export not starting.\n" +
            $"Stdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    /// <summary>
    /// Asserts the combined output references the subprocess exit code that was propagated.
    /// Covers scenario 5 (subprocess exit-code propagation).
    /// </summary>
    public TfsExportResult AssertSubprocessExitCodeReferencedInOutput(int expectedCode)
    {
        Assert.IsTrue(
            CombinedOutput.Contains(expectedCode.ToString()),
            $"Expected output to reference subprocess exit code {expectedCode}.\n" +
            $"Stdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    // ── cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => _disposeAsync();
}
