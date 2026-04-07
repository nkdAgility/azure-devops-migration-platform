using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Services;

/// <summary>
/// Drives the interactive terminal prompts for configuring a migration and returns
/// the resulting <see cref="MigrationOptions"/>. Separated from the command class
/// so it can be tested without a real CLI host.
/// </summary>
internal interface IInteractiveConfigurationBuilder
{
    Task<MigrationOptions> BuildAsync(IAnsiConsole console, CancellationToken cancellationToken);
}
