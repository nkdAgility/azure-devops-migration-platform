// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli;

[TestClass]
public class MigrateLogsDslTests
{
    private static readonly Guid s_snapshotJobId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid s_followJobId   = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid s_httpErrJobId  = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid s_followErrId   = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid s_forbiddenId   = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static readonly List<ProgressEvent> s_twoEvents =
    [
        new() { Module = "workitems", Stage = "Stage1" },
        new() { Module = "workitems", Stage = "Stage2" }
    ];

    private static async IAsyncEnumerable<ProgressEvent> YieldEventsAsync(
        IEnumerable<ProgressEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
            await Task.Yield();
        }
    }

#pragma warning disable CS0162
    private static async IAsyncEnumerable<ProgressEvent> ThrowAsync(
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        await Task.Yield();
        throw new HttpRequestException("500 Server Error");
        yield break;
    }
#pragma warning restore CS0162

    private static async Task<(int exitCode, string stdout)> RunAsync(
        Guid jobId, bool follow, Mock<ILogsClient> client, CancellationToken ct = default)
    {
        var stdout = new StringWriter();
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(stdout),
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILogsClient>(client.Object);
                services.AddSingleton<IAnsiConsole>(testConsole);
            })
            .Build();

        var command = new LogsCommand();
        command.Host = host;

        var settings = new LogsCommand.Settings { JobId = jobId, Follow = follow };
        var remaining = new Mock<IRemainingArguments>();
        remaining.Setup(r => r.Raw).Returns(Array.Empty<string>());
        remaining.Setup(r => r.Parsed).Returns(Enumerable.Empty<string>().ToLookup(x => x, x => (string?)x));
        var cmdCtx = new CommandContext(Array.Empty<string>(), remaining.Object, "logs", null);

        var executeMethod = typeof(LogsCommand).GetMethod(
            "ExecuteInternalAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var exitCode = await (Task<int>)executeMethod.Invoke(command, new object[] { cmdCtx, settings, ct })!;
        return (exitCode, stdout.ToString());
    }

    // ── Scenario: Snapshot mode prints NDJSON lines and exits 0 ───────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task LogsCommand_SnapshotMode_PrintsJsonLinesAndExits0()
    {
        var client = new Mock<ILogsClient>(MockBehavior.Strict);
        client.Setup(c => c.GetProgressAsync(s_snapshotJobId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(s_twoEvents);

        var (exitCode, stdout) = await RunAsync(s_snapshotJobId, follow: false, client);

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stdout), "Expected JSON lines in stdout.");
    }

    // ── Scenario: Follow mode streams live events until the job completes ──────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task LogsCommand_FollowMode_StreamsLiveEventsAndExits0()
    {
        var client = new Mock<ILogsClient>(MockBehavior.Strict);
        client.Setup(c => c.FollowLogsAsync(s_followJobId, It.IsAny<CancellationToken>(), It.IsAny<long?>()))
              .Returns<Guid, CancellationToken, long?>((_, ct, _) => YieldEventsAsync(s_twoEvents, ct));

        var (exitCode, stdout) = await RunAsync(s_followJobId, follow: true, client);

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stdout), "Expected JSON lines in stdout.");
    }

    // ── Scenario: HTTP error in snapshot mode exits 1 ─────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task LogsCommand_SnapshotMode_HttpError_Exits1()
    {
        var client = new Mock<ILogsClient>(MockBehavior.Strict);
        client.Setup(c => c.GetProgressAsync(s_httpErrJobId, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));

        var (exitCode, stdout) = await RunAsync(s_httpErrJobId, follow: false, client);

        Assert.AreEqual(1, exitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stdout), "An error message must be printed to stdout on HTTP error.");
    }

    // ── Scenario: HTTP error in follow mode exits 1 ───────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task LogsCommand_FollowMode_HttpError_Exits1()
    {
        var client = new Mock<ILogsClient>(MockBehavior.Strict);
        client.Setup(c => c.FollowLogsAsync(s_followErrId, It.IsAny<CancellationToken>(), It.IsAny<long?>()))
              .Returns<Guid, CancellationToken, long?>((_, _, _) => ThrowAsync());

        var (exitCode, stdout) = await RunAsync(s_followErrId, follow: true, client);

        Assert.AreEqual(1, exitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stdout), "An error message must be printed to stdout on HTTP error.");
    }

    // ── Scenario: HTTP 403 causes a permissions error message ────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task LogsCommand_SnapshotMode_Http403_Exits1()
    {
        var client = new Mock<ILogsClient>(MockBehavior.Strict);
        client.Setup(c => c.GetProgressAsync(s_forbiddenId, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("403 Forbidden"));

        var (exitCode, stdout) = await RunAsync(s_forbiddenId, follow: false, client);

        Assert.AreEqual(1, exitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stdout), "An error message must be printed to stdout on HTTP 403.");
    }
}
