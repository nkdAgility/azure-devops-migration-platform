// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Migration.Views;
using DevOpsMigrationPlatform.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Interactive wizard to create a new migration configuration file.
/// Replaces the former <c>configure</c> command; now lives under <c>config new</c>.
/// </summary>
public sealed class ConfigNewCommand : CommandBase<ConfigNewCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        ConfigNewCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<IInteractiveConfigurationBuilder, InteractiveConfigurationBuilder>();
            services.AddSingleton<ConfigureCommandRenderer>();
        });

        var console = GetRequiredService<IAnsiConsole>();
        var builder = GetRequiredService<IInteractiveConfigurationBuilder>();
        var renderer = GetRequiredService<ConfigureCommandRenderer>();
        var configService = GetRequiredService<IConfigurationService>();

        console.Clear();
        renderer.ShowBanner(console);

        var outputFile = settings.OutputFile ?? "migration.json";

        if (File.Exists(outputFile) && !settings.Force)
        {
            if (!Confirm($"Configuration file '{outputFile}' already exists. Overwrite?", false))
            {
                ShowInfo(console, "Configuration setup cancelled.");
                return 0;
            }
        }

        try
        {
            var options = await builder.BuildAsync(console, cancellationToken);
            await configService.SaveConfigurationAsync(options, outputFile, cancellationToken);
            ShowSuccess(console, $"Configuration saved to: {outputFile}");
            renderer.ShowCompletionSummary(options, outputFile, console);
            return 0;
        }
        catch (OperationCanceledException)
        {
            ShowWarning(console, "Configuration setup was cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            ShowError(console, $"Configuration setup failed: {ex.Message}");
            return 1;
        }
    }

    protected override bool RequiresConfigurationValidation() => false;
}
