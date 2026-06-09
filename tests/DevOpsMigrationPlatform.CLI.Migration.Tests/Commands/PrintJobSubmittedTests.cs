// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class PrintJobSubmittedTests
{
    [TestCategory("UnitTest")]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PrintJobSubmitted_WritesJobIdLine()
    {
        // Arrange
        var console = new TestConsole();
        var jobId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var url = "http://localhost:5100";

        // Act
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;

        // Assert
        Assert.IsTrue(output.Contains("Job ID"), $"Expected 'Job ID' in output. Got:\n{output}");
        Assert.IsTrue(output.Contains(jobId.ToString()), $"Expected job ID value in output. Got:\n{output}");
    }

    [TestCategory("UnitTest")]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PrintJobSubmitted_WritesControlPlaneUrlLine()
    {
        // Arrange
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "https://my-control-plane.example.com";

        // Act
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;

        // Assert
        Assert.IsTrue(output.Contains("Control"), $"Expected 'Control' in output. Got:\n{output}");
        Assert.IsTrue(output.Contains(url), $"Expected URL value in output. Got:\n{output}");
    }

    [TestCategory("UnitTest")]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PrintJobSubmitted_JobIdLineAppearsBeforeControlLine()
    {
        // Arrange
        var console = new TestConsole();
        var jobId = Guid.NewGuid();
        var url = "http://localhost:5100";

        // Act
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, jobId, url);
        var output = console.Output;

        // Assert (SC-004: job ID line must appear before control URL line)
        var jobIdIndex = output.IndexOf("Job ID", StringComparison.Ordinal);
        var controlIndex = output.IndexOf("Control", StringComparison.Ordinal);
        Assert.IsTrue(jobIdIndex < controlIndex,
            $"Expected 'Job ID' line before 'Control' line. Got:\n{output}");
    }

    // --- Scenario 1: Standalone mode shows local control plane URL ---

    [TestCategory("UnitTest")]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void StandaloneMode_ShowsLocalControlPlaneUrl_AlongsideJobId()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var fixture = new JobSubmissionOutputFixture()
            .WithStandaloneMode()
            .WithJobId(jobId);

        // Act
        var result = fixture.ActJobAccepted();

        // Assert
        result
            .ShouldContainJobId()
            .ShouldContainControlPlaneUrl()   // expects http://localhost:5100
            .ShouldShowJobIdBeforeControlPlaneUrl();
    }

    // --- Scenario 2: Remote mode shows the supplied --url ---

    [TestCategory("UnitTest")]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RemoteMode_ShowsSuppliedUrl_AlongsideJobId()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var remoteUrl = "https://my-control-plane.example.com";
        var fixture = new JobSubmissionOutputFixture()
            .WithRemoteUrl(remoteUrl)
            .WithJobId(jobId);

        // Act
        var result = fixture.ActJobAccepted();

        // Assert
        result
            .ShouldContainJobId()
            .ShouldContainControlPlaneUrl();  // expects https://my-control-plane.example.com
    }

    // --- Scenario 3: Submission failure still shows the attempted URL ---

    [TestCategory("UnitTest")]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SubmissionFailure_ShowsAttemptedUrl_InErrorOutput()
    {
        // Arrange
        var controlPlaneUrl = "https://my-control-plane.example.com";
        var fixture = new JobSubmissionOutputFixture()
            .WithRemoteUrl(controlPlaneUrl);

        // Act
        var result = fixture.ActSubmissionFailed();

        // Assert
        result.ShouldShowAttemptedUrlInErrorOutput();
    }
}

/// <summary>Exposes protected static <c>PrintJobSubmitted</c> for testing.</summary>
internal sealed class TestControlPlaneCommandBase : ControlPlaneCommandBase<DevOpsMigrationPlatform.CLI.Migration.Settings.ControlPlaneBaseCommandSettings>
{
    public static void InvokePrintJobSubmitted(IAnsiConsole console, Guid jobId, string url)
        => PrintJobSubmitted(console, jobId, url);

    protected override System.Threading.Tasks.Task<int> ExecuteInternalAsync(
        Spectre.Console.Cli.CommandContext context,
        DevOpsMigrationPlatform.CLI.Migration.Settings.ControlPlaneBaseCommandSettings settings,
        System.Threading.CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(0);
}

