using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>Fetches or streams ProgressEvent records. --follow opens the SSE stream.</summary>
public sealed class ManageLogsCommand : CommandBase<ManageLogsCommand.Settings>
{
    public sealed class Settings : BaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to retrieve logs for")]
        public string? JobId { get; init; }

        [CommandOption("--follow")]
        [Description("Follow live events via SSE until the job completes")]
        public bool Follow { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage logs — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
