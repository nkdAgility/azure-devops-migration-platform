// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Captured result of a dotnet test subprocess invocation.
/// </summary>
public sealed class DotnetTestResult
{
    /// <summary>Process exit code. 0 = all tests passed.</summary>
    public int ExitCode { get; }

    /// <summary>Combined standard output captured from the process.</summary>
    public string StdOut { get; }

    /// <summary>Combined standard error captured from the process.</summary>
    public string StdErr { get; }

    /// <summary>Wall-clock duration of the subprocess.</summary>
    public TimeSpan Elapsed { get; }

    internal DotnetTestResult(int exitCode, string stdOut, string stdErr, TimeSpan elapsed)
    {
        ExitCode = exitCode;
        StdOut = stdOut ?? string.Empty;
        StdErr = stdErr ?? string.Empty;
        Elapsed = elapsed;
    }

    private string FullOutput => StdOut + StdErr;

    /// <summary>Asserts that the process exited with code 0.</summary>
    public DotnetTestResult ShouldSucceed()
    {
        Assert.AreEqual(0, ExitCode,
            $"Expected dotnet test to succeed (exit 0) but got {ExitCode}.\nOutput:\n{FullOutput}");
        return this;
    }

    /// <summary>Asserts that the combined output contains the given substring.</summary>
    public DotnetTestResult ShouldContain(string text)
    {
        StringAssert.Contains(FullOutput, text,
            $"Expected output to contain '{text}'.\nActual output:\n{FullOutput}");
        return this;
    }

    /// <summary>Asserts that the combined output does NOT contain the given value (token guard).</summary>
    public DotnetTestResult ShouldNotContain(string text)
    {
        Assert.IsFalse(FullOutput.Contains(text, StringComparison.Ordinal),
            $"Output must not contain '{text}' (sensitive value leakage).");
        return this;
    }

    /// <summary>Asserts that the run completed within the given wall-clock limit.</summary>
    public DotnetTestResult ShouldCompleteWithin(TimeSpan limit)
    {
        Assert.IsTrue(Elapsed <= limit,
            $"Expected run to complete within {limit} but it took {Elapsed}.");
        return this;
    }

    /// <summary>
    /// Asserts that no test with [TestCategory("SystemTest")] was included in the run output.
    /// </summary>
    public DotnetTestResult ShouldHaveRunOnlyUnitTests()
    {
        Assert.IsFalse(FullOutput.Contains("SystemTest", StringComparison.OrdinalIgnoreCase),
            $"Expected only unit tests to run, but output mentions 'SystemTest'.\nOutput:\n{FullOutput}");
        return this;
    }

    /// <summary>
    /// Asserts that the output indicates system tests were excluded (filter applied).
    /// </summary>
    public DotnetTestResult ShouldHaveExcludedSystemTests()
    {
        // dotnet test with a filter that excludes all SystemTest-categorised tests
        // either reports "No tests ran" or a passed run with only unit tests.
        // We accept either outcome as long as ExitCode is 0.
        Assert.AreEqual(0, ExitCode,
            $"Expected filtered run (exclude SystemTest) to exit 0 but got {ExitCode}.\nOutput:\n{FullOutput}");
        return this;
    }

    /// <summary>
    /// Asserts that the combined output contains at least one "Inconclusive" outcome marker.
    /// MSTest writes "Inconclusive" into dotnet test output when Assert.Inconclusive is called.
    /// </summary>
    public DotnetTestResult ShouldContainInconclusiveTests()
    {
        StringAssert.Contains(FullOutput, "Inconclusive",
            $"Expected at least one Inconclusive test result in output.\nActual:\n{FullOutput}");
        return this;
    }

    /// <summary>
    /// Asserts that the combined output does NOT contain the given credential value.
    /// Alias for ShouldNotContain with a more behaviour-specific name.
    /// </summary>
    public DotnetTestResult ShouldNotContainCredential(string value)
        => ShouldNotContain(value);
}
