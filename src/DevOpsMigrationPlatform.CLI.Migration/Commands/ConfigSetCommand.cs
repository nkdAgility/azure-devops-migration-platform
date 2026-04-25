using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Sets a user-level preference key/value in <c>preferences.json</c>.
/// Usage: <c>devopsmigration config set &lt;key&gt; &lt;value&gt;</c>
/// </summary>
public sealed class ConfigSetCommand : AsyncCommand<ConfigSetCommandSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, ConfigSetCommandSettings settings, CancellationToken cancellationToken = default)
    {
        var key = settings.Key;
        var value = settings.Value;

        if (UserPreferencesService.Set(key, value))
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Set [cyan]{key}[/] = [cyan]{value}[/]");
            AnsiConsole.MarkupLine($"[dim]Stored in: {UserPreferencesService.GetPreferencesFilePath()}[/]");
            return Task.FromResult(0);
        }

        AnsiConsole.MarkupLine($"[red]✗[/] Unknown preference key: [cyan]{key}[/]");
        AnsiConsole.MarkupLine("[dim]Supported keys:[/]");
        foreach (var supported in UserPreferencesService.SupportedKeys)
            AnsiConsole.MarkupLine($"  • {supported}");

        return Task.FromResult(1);
    }
}
