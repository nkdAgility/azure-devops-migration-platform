using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using Moq;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Cli;

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
        return new LogsCommand(ClientMock.Object, ActivitySource);
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
