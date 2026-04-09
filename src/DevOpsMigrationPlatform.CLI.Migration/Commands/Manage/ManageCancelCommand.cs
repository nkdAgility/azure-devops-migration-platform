using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>Cancels a queued or running job.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class ManageCancelCommand : ControlPlaneCommandBase<ManageCancelCommand.Settings>
{
    public sealed class Settings : ControlPlaneBaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to cancel")]
        public string? JobId { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage cancel — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
