using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Migration.Views;
using DevOpsMigrationPlatform.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Interactive command to help users create and configure migration settings.
/// Guides users through the setup process and generates configuration files.
/// </summary>
public sealed class ConfigureCommand : CommandBase<ConfigureCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(CommandContext context, ConfigureCommandSettings settings, CancellationToken cancellationToken = default)
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

        if (!string.IsNullOrWhiteSpace(settings.OutputFile) && File.Exists(settings.OutputFile) && !settings.Force)
        {
            if (!Confirm($"Configuration file '{settings.OutputFile}' already exists. Overwrite?", false))
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

    protected override bool RequiresConfigurationValidation() => false; // We're creating the configuration
}

/// <summary>
/// Settings for the configure command.
/// </summary>
public class ConfigureCommandSettings : BaseCommandSettings
{
    [CommandOption("-o|--output")]
    [Description("Output file for the configuration (default: migration.json)")]
    public string? OutputFile { get; set; }

    [CommandOption("-f|--force")]
    [Description("Overwrite existing configuration file without prompting")]
    public bool Force { get; set; }
}