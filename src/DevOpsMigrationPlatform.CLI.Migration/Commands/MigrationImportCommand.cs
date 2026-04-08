using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>Submits an import-only job. Reads the package from artefacts.path in config.</summary>
public sealed class MigrationImportCommand : CommandBase<MigrationImportCommandSettings>
{
    protected override Task<int> ExecuteInternalAsync(CommandContext context, MigrationImportCommandSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]import — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
