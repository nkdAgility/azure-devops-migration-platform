using System;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

/// <summary>
/// System-level test asserting observable CLI output ordering (SC-004).
/// Verifies that the Job ID line appears before the Control Plane URL line
/// in the output produced by export job submission.
/// </summary>
[TestClass]
[TestCategory("SystemTest")]
public class TuiSystemTests
{
    /// <summary>
    /// SC-004: The Job ID output line must appear BEFORE the Control Plane URL line.
    /// </summary>
    [TestMethod]
    public void PrintJobSubmitted_OutputContainsJobIdLine_SC004()
    {
        // Arrange
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "http://localhost:5100";

        // Act
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;

        // Assert — Job ID line is present
        Assert.IsTrue(
            output.Contains("Job ID"),
            $"Expected 'Job ID' label in output. Got:\n{output}");
    }

    /// <summary>
    /// SC-004: Control Plane URL line must appear in output.
    /// </summary>
    [TestMethod]
    public void PrintJobSubmitted_OutputContainsControlLine_SC004()
    {
        // Arrange
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "https://my-control-plane.example.com";

        // Act
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;

        // Assert — Control line is present
        Assert.IsTrue(
            output.Contains("Control"),
            $"Expected 'Control' label in output. Got:\n{output}");
        Assert.IsTrue(
            output.Contains(url),
            $"Expected URL value '{url}' in output. Got:\n{output}");
    }

    /// <summary>
    /// SC-004: The Job ID line must appear BEFORE the Control Plane URL line in stdout.
    /// </summary>
    [TestMethod]
    public void PrintJobSubmitted_JobIdLineBeforeControlLine_SC004()
    {
        // Arrange
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "http://localhost:5100";

        // Act
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;

        // Assert
        var jobIdLinePos = output.IndexOf("Job ID", StringComparison.Ordinal);
        var controlLinePos = output.IndexOf("Control", StringComparison.Ordinal);

        Assert.IsTrue(jobIdLinePos >= 0, $"'Job ID' not found in output:\n{output}");
        Assert.IsTrue(controlLinePos >= 0, $"'Control' not found in output:\n{output}");
        Assert.IsTrue(
            jobIdLinePos < controlLinePos,
            $"'Job ID' line (pos {jobIdLinePos}) must appear before 'Control' line (pos {controlLinePos}).\n{output}");
    }
}
