using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>Full lifecycle: export → validate → import in one orchestrated run.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class MigrationMigrateCommand : ControlPlaneCommandBase<MigrationMigrateCommandSettings>
{
    protected override Task<int> ExecuteInternalAsync(CommandContext context, MigrationMigrateCommandSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]migrate — not available in this release.[/]");
        return Task.FromResult(1);
    }
}
