using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Interactive command to help users create and configure migration settings.
/// Guides users through the setup process and generates configuration files.
/// </summary>
public sealed class ConfigureCommand : CommandBase<ConfigureCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(CommandContext context, ConfigureCommandSettings settings, CancellationToken cancellationToken = default)
    {
        // Create command-specific host with configuration service
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddSingleton<IConfigurationService, ConfigurationService>();
        });

        var console = GetRequiredService<IAnsiConsole>();

        console.Clear();
        ShowBanner(console);

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
            var configuration = await InteractiveSetupAsync(settings, cancellationToken);
            await SaveConfigurationAsync(configuration, settings, cancellationToken);
            ShowCompletionSummary(configuration, settings);

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

    /// <summary>
    /// Displays application banner and introduction.
    /// </summary>
    private void ShowBanner(IAnsiConsole console)
    {
        console.Write(new FigletText("Config Setup").Centered().Color(Color.Blue));
        console.Write(new Rule().RuleStyle("grey"));
        console.MarkupLine("[dim]Azure DevOps Migration Platform Configuration Wizard[/]");
        console.WriteLine();

        console.MarkupLine("This wizard will guide you through creating a migration configuration file.");
        console.MarkupLine("You can modify the generated file later or create additional configurations for different scenarios.");
        console.WriteLine();
    }

    /// <summary>
    /// Interactive setup flow that guides user through configuration options.
    /// </summary>
    private async Task<MigrationOptions> InteractiveSetupAsync(ConfigureCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var options = new MigrationOptions();

        // Step 1: Mode
        console.MarkupLine("[bold blue]Step 1: Migration Mode[/bold blue]");
        options.Mode = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select migration mode:")
                .AddChoices("Export", "Import", "Both"));
        console.WriteLine();

        var isExport = options.Mode is "Export" or "Both";
        var isImport = options.Mode is "Import" or "Both";

        // Step 2: Source (if Export or Both)
        if (isExport)
        {
            console.MarkupLine("[bold blue]Step 2: Source System Configuration[/bold blue]");
            options.Source = await ConfigureEndpointAsync(console, "source", cancellationToken);
            console.WriteLine();
        }

        // Step 3: Target (if Import or Both)
        if (isImport)
        {
            console.MarkupLine($"[bold blue]Step {(isExport ? 3 : 2)}: Target System Configuration[/bold blue]");
            options.Target = await ConfigureEndpointAsync(console, "target", cancellationToken);
            console.WriteLine();
        }

        // Step 4: Artefacts path
        var stepNum = 2 + (isExport ? 1 : 0) + (isImport ? 1 : 0);
        console.MarkupLine($"[bold blue]Step {stepNum}: Package Storage[/bold blue]");
        options.Artefacts.Path = console.Prompt(
            new TextPrompt<string>("Migration package directory:")
                .DefaultValue(options.Artefacts.Path));
        options.Artefacts.Zip = console.Confirm("Compress the package (zip)?", defaultValue: false);

        return options;
    }

    /// <summary>
    /// Configures a source or target endpoint.
    /// </summary>
    private async Task<MigrationEndpointOptions> ConfigureEndpointAsync(IAnsiConsole console, string role, CancellationToken cancellationToken)
    {
        var endpoint = new MigrationEndpointOptions();

        var types = new[] { "AzureDevOpsServices", "TeamFoundationServer" };
        endpoint.Type = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select {role} system type:")
                .AddChoices(types));

        var defaultUrl = endpoint.Type switch
        {
            "AzureDevOpsServices" => "https://dev.azure.com/[organization]",
            _ => "http://[server]:8080/tfs/[collection]"
        };

        endpoint.OrgOrCollection = console.Ask<string>($"Enter {role} URL [{defaultUrl}]:");
        endpoint.Project = console.Ask<string>($"Enter {role} project name:");

        // Authentication
        var authTypes = new[] { "Pat", "Windows" };
        var authType = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select {role} authentication type:")
                .AddChoices(authTypes));

        if (authType == "Pat")
        {
            var accessToken = console.Prompt(
                new TextPrompt<string>($"Enter {role} Personal Access Token (or $ENV:VARNAME):")
                    .Secret());

            endpoint.Authentication = new EndpointAuthenticationOptions
            {
                Type = "Pat",
                AccessToken = accessToken
            };
        }
        else
        {
            endpoint.Authentication = new EndpointAuthenticationOptions { Type = "Windows" };
        }

        return endpoint;
    }

    /// <summary>
    /// Saves the configuration to file.
    /// </summary>
    private async Task SaveConfigurationAsync(MigrationOptions options, ConfigureCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var configService = GetRequiredService<IConfigurationService>();

        var outputFile = settings.OutputFile ?? "migration.json";

        await configService.SaveConfigurationAsync(options, outputFile, cancellationToken);
        ShowSuccess(console, $"Configuration saved to: {outputFile}");
    }

    /// <summary>
    /// Shows completion summary and next steps.
    /// </summary>
    private void ShowCompletionSummary(MigrationOptions options, ConfigureCommandSettings settings)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var outputFile = settings.OutputFile ?? "migration.json";

        console.WriteLine();
        console.Write(new Rule("[bold green]Configuration Complete![/]").RuleStyle("green"));
        console.WriteLine();

        console.MarkupLine("[green]✓[/] Configuration file created successfully");
        console.MarkupLine($"[blue]📁 File:[/] {outputFile}");
        console.MarkupLine($"[blue]📋 Mode:[/] {options.Mode}");
        console.WriteLine();

        console.MarkupLine("[bold]Next Steps:[/]");
        console.MarkupLine($"• Run discovery: [cyan]devopsmigration discovery inventory --config {outputFile}[/]");

        if (options.Mode is "Export" or "Both")
            console.MarkupLine($"• Run export: [cyan]devopsmigration export --config {outputFile}[/]");

        if (options.Mode is "Import" or "Both")
            console.MarkupLine($"• Run import: [cyan]devopsmigration import --config {outputFile}[/]");

        console.WriteLine();
        console.MarkupLine("[dim]Tip: You can edit the configuration file directly to fine-tune settings.[/]");
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