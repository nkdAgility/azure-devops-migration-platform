using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>Lists all jobs visible to the authenticated user with status and progress.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class ManageListCommand : CommandBase<BaseCommandSettings>
{
    protected override Task<int> ExecuteInternalAsync(CommandContext context, BaseCommandSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage list — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
