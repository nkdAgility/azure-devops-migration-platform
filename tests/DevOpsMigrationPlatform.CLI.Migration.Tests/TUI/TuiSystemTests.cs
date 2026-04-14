using System;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

/// <summary>
/// Unit tests asserting observable CLI output ordering (SC-004).
/// These tests run entirely in-process using TestConsole and
/// do NOT require external services; they must NOT be marked [TestCategory("SystemTest")].
/// </summary>
[TestClass]
public class TuiSystemTests
{
    [TestMethod]
    public void PrintJobSubmitted_OutputContainsJobIdLine_SC004()
    {
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "http://localhost:5100";
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;
        Assert.IsTrue(output.Contains("Job ID"), $"Expected 'Job ID' label in output. Got:\n{output}");
    }

    [TestMethod]
    public void PrintJobSubmitted_OutputContainsControlLine_SC004()
    {
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "https://my-control-plane.example.com";
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;
        Assert.IsTrue(output.Contains("Control"), $"Expected 'Control' label in output. Got:\n{output}");
        Assert.IsTrue(output.Contains(url), $"Expected URL value '{url}' in output. Got:\n{output}");
    }

    [TestMethod]
    public void PrintJobSubmitted_JobIdLineBeforeControlLine_SC004()
    {
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "http://localhost:5100";
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;
        var jobIdLinePos = output.IndexOf("Job ID", StringComparison.Ordinal);
        var controlLinePos = output.IndexOf("Control", StringComparison.Ordinal);
        Assert.IsTrue(jobIdLinePos >= 0, $"'Job ID' not found in output:\n{output}");
        Assert.IsTrue(controlLinePos >= 0, $"'Control' not found in output:\n{output}");
        Assert.IsTrue(
            jobIdLinePos < controlLinePos,
            $"'Job ID' line (pos {jobIdLinePos}) must appear before 'Control' line (pos {controlLinePos}).\n{output}");
    }
}
