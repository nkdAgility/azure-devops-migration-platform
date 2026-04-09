using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>Revokes the stored session token for a control plane endpoint.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class ManageLogoutCommand : CommandBase<ManageLogoutCommand.Settings>
{
    public sealed class Settings : BaseCommandSettings
    {
        [CommandOption("--url <URL>")]
        [Description("Control plane endpoint URL")]
        public string? Url { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage logout — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
