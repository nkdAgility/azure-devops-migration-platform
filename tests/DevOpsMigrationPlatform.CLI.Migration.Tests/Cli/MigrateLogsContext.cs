// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli;

internal sealed class MigrateLogsContext : IDisposable
{
    public Mock<ILogsClient> ClientMock { get; } = new(MockBehavior.Strict);
    public ActivitySource ActivitySource { get; } = new("DevOpsMigrationPlatform.CLI");
    public StringWriter StdoutCapture { get; } = new();
    public StringWriter StderrCapture { get; } = new();
    public int ExitCode { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    public LogsCommand BuildCommand()
    {
        var command = new LogsCommand();

        // Build a minimal host with mock services so the command skips CreateHost
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(StdoutCapture),
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILogsClient>(ClientMock.Object);
                services.AddSingleton<IAnsiConsole>(testConsole);
            })
            .Build();

        command.Host = host;
        return command;
    }

    public void Dispose()
    {
        StdoutCapture.Dispose();
        StderrCapture.Dispose();
        ActivitySource.Dispose();
        Cts.Dispose();
    }

    public static async IAsyncEnumerable<ProgressEvent> YieldEventsAsync(
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
}
