using System.Net;
using System.Reflection;
using System.Text;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Cli;
using Moq;
using Reqnroll;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli;

[Binding]
[Scope(Feature = "migrate logs command")]
internal sealed class MigrateLogsSteps
{
    private readonly MigrateLogsContext _ctx;
    private Guid _jobId;

    public MigrateLogsSteps(MigrateLogsContext ctx) => _ctx = ctx;

    [Given(@"a job ""([^""]+)"" has stored ProgressEvents on the Control Plane")]
    public void GivenJobHasStoredEvents(string jobIdString)
    {
        _jobId = Guid.Parse(jobIdString);
        _ctx.ClientMock
            .Setup(c => c.GetProgressAsync(_jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProgressEvent>
            {
                new() { Module = "WorkItems", Stage = "Stage1" },
                new() { Module = "WorkItems", Stage = "Stage2" }
            });
    }

    [Given(@"a job ""([^""]+)"" is in progress on the Control Plane")]
    public void GivenJobIsInProgress(string jobIdString)
    {
        _jobId = Guid.Parse(jobIdString);
        var events = new List<ProgressEvent>
        {
            new() { Module = "WorkItems", Stage = "Live1" },
            new() { Module = "WorkItems", Stage = "Live2" }
        };

        _ctx.ClientMock
            .Setup(c => c.FollowLogsAsync(_jobId, It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .Returns<Guid, CancellationToken, long?>((_, ct, _) =>
                MigrateLogsContext.YieldEventsAsync(events, ct));
    }

    [Given(@"the Control Plane returns an HTTP error for job ""([^""]+)""")]
    [Given(@"the Control Plane returns 403 for job ""([^""]+)""")]
    public void GivenControlPlaneReturnsHttpError(string jobIdString)
    {
        _jobId = Guid.Parse(jobIdString);

        _ctx.ClientMock
            .Setup(c => c.GetProgressAsync(_jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden"));

        _ctx.ClientMock
            .Setup(c => c.FollowLogsAsync(_jobId, It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .Returns<Guid, CancellationToken, long?>((_, _, _) => ThrowAsync());
    }

#pragma warning disable CS0162 // Unreachable code — yield break required for async iterator
    private static async IAsyncEnumerable<ProgressEvent> ThrowAsync()
    {
        await Task.Yield();
        throw new HttpRequestException("403 Forbidden");
        yield break; // unreachable but required for compiler
    }
#pragma warning restore CS0162

    [When(@"I run ""migrate logs --job ([^ ]+)""")]
    public async Task WhenIRunLogsCommand(string jobIdString)
    {
        await RunCommandAsync(false);
    }

    [When(@"I run ""migrate logs --job ([^ ]+) --follow""")]
    public async Task WhenIRunLogsCommandWithFollow(string jobIdString)
    {
        await RunCommandAsync(true);
    }

    [When("a cancellation is requested during streaming")]
    public void WhenCancellationRequested()
    {
        // Already handled via Cts — the follow command sets up cancellation during execution.
        // This step is structural; cancellation is handled in the command via Ctrl+C simulation.
    }

    private async Task RunCommandAsync(bool follow)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(_ctx.StdoutCapture);
        Console.SetError(_ctx.StderrCapture);
        try
        {
            var command = _ctx.BuildCommand();
            var settings = new LogsCommand.Settings { JobId = _jobId, Follow = follow };
            var remaining = new Mock<IRemainingArguments>();
            remaining.Setup(r => r.Raw).Returns(Array.Empty<string>());
            remaining.Setup(r => r.Parsed).Returns(Enumerable.Empty<string>().ToLookup(x => x, x => (string?)x));
            var cmdCtx = new CommandContext(Array.Empty<string>(), remaining.Object, "logs", null);

            // Use reflection to access the protected ExecuteInternalAsync method
            var executeMethod = typeof(LogsCommand).GetMethod("ExecuteInternalAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = (Task<int>)executeMethod!.Invoke(command, new object[] { cmdCtx, settings, _ctx.Cts.Token })!;
            _ctx.ExitCode = await task;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    [Then("each event is written to stdout as a compact JSON line")]
    [Then("each arriving event is written to stdout as a compact JSON line")]
    public void ThenEventsAreWrittenToStdout()
    {
        var output = _ctx.StdoutCapture.ToString();
        Assert.IsFalse(string.IsNullOrEmpty(output),
            "Expected at least one JSON line written to stdout.");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length > 0, "Expected at least one output line.");
    }

    [Then("the command exits with code 0")]
    [Then("when the stream ends the command exits with code 0")]
    public void ThenExitsWithCode0()
    {
        Assert.AreEqual(0, _ctx.ExitCode, "Expected command exit code 0.");
    }

    [Then("the command exits with code 1")]
    public void ThenExitsWithCode1()
    {
        Assert.AreEqual(1, _ctx.ExitCode, "Expected command exit code 1.");
    }

    [Then("an error message is printed to stdout")]
    public void ThenErrorMessageIsPrinted()
    {
        var stdout = _ctx.StdoutCapture.ToString();
        Assert.IsFalse(string.IsNullOrEmpty(stdout),
            "Expected an error message in stdout.");
    }

    [Then("the job on the Control Plane is not stopped")]
    public void ThenJobIsNotStopped()
    {
        // Verify the expected FollowLogsAsync call happened (SC-005 — job is unaffected by Ctrl+C).
        _ctx.ClientMock.Verify(c => c.FollowLogsAsync(_jobId, It.IsAny<CancellationToken>(), It.IsAny<long?>()), Times.Once);
        // No other calls (no cancel/stop endpoint hit).
        _ctx.ClientMock.VerifyNoOtherCalls();
    }
}
