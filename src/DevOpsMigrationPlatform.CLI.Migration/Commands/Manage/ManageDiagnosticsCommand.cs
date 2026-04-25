using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>
/// Reports the location of diagnostic logs for a completed job.
/// Diagnostic logs are stored in the job's working directory under
/// <c>.migration/Logs/&lt;ticks&gt;-&lt;jobId&gt;/agent.jsonl</c>.
/// See docs/cli.md for full specification.
/// </summary>
public sealed class ManageDiagnosticsCommand : ControlPlaneCommandBase<ManageDiagnosticsCommand.Settings>
{
    public sealed class Settings : ControlPlaneBaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to retrieve diagnostic logs for.")]
        public string? JobId { get; init; }

        [CommandOption("--level")]
        [Description("Client-side level filter. Only show records at this level or above. Default: all.")]
        public string? Level { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[yellow]Diagnostic logs are stored in the job's working directory:[/]");
        AnsiConsole.MarkupLine("[grey].migration/Logs/<ticks>-<jobId>/agent.jsonl[/]");
        AnsiConsole.MarkupLine("[grey]Use the --job option to identify the correct job folder.[/]");
        if (!string.IsNullOrWhiteSpace(settings.JobId))
            AnsiConsole.MarkupLine($"[grey]Job ID: {settings.JobId}[/]");
        return Task.FromResult(0);
    }
}
