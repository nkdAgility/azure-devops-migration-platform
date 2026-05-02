// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Reads and prints a user-level preference value from <c>preferences.json</c>.
/// Usage: <c>devopsmigration config get &lt;key&gt;</c>
/// </summary>
public sealed class ConfigGetCommand : AsyncCommand<ConfigGetCommandSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, ConfigGetCommandSettings settings, CancellationToken cancellationToken = default)
    {
        var key = settings.Key;
        var value = UserPreferencesService.Get(key);

        if (value is not null)
        {
            AnsiConsole.MarkupLine($"[cyan]{key}[/] = [cyan]{value}[/]");
            return Task.FromResult(0);
        }

        if (!UserPreferencesService.SupportedKeys.Contains(key.ToLowerInvariant()))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown preference key: [cyan]{key}[/]");
            AnsiConsole.MarkupLine("[dim]Supported keys:[/]");
            foreach (var supported in UserPreferencesService.SupportedKeys)
                AnsiConsole.MarkupLine($"  • {supported}");
            return Task.FromResult(1);
        }

        AnsiConsole.MarkupLine($"[yellow]⚠[/] [cyan]{key}[/] is not set.");
        return Task.FromResult(0);
    }
}
