using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>Validates config, computes configHash, and prints planned modules. No job is submitted.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class MigrationPrepareCommand : ControlPlaneCommandBase<MigrationCommandSettings>
{
    protected override Task<int> ExecuteInternalAsync(CommandContext context, MigrationCommandSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]prepare — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
