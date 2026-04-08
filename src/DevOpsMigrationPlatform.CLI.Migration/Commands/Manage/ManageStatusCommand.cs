using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>Displays job state and per-module progress for a specific job.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class ManageStatusCommand : CommandBase<ManageStatusCommand.Settings>
{
    public sealed class Settings : BaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to query")]
        public string? JobId { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage status — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
