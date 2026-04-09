using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>Opens the interactive Terminal UI showing live job state.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class TuiCommand : ControlPlaneCommandBase<ControlPlaneBaseCommandSettings>
{
    protected override Task<int> ExecuteInternalAsync(CommandContext context, ControlPlaneBaseCommandSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]tui — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
