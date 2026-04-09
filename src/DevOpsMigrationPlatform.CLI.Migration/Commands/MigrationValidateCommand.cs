using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>Runs pre-flight validation on an existing package.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class MigrationValidateCommand : ControlPlaneCommandBase<MigrationValidateCommandSettings>
{
    protected override Task<int> ExecuteInternalAsync(CommandContext context, MigrationValidateCommandSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]validate — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
