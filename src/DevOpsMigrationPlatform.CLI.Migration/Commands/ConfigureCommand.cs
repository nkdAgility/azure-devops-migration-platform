using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Interactive command to help users create and configure migration settings.
/// Guides users through the setup process and generates configuration files.
/// </summary>
public sealed class ConfigureCommand : CommandBase<ConfigureCommandSettings>
{
    public ConfigureCommand(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime,
        ILogger<ConfigureCommand> logger,
        ActivitySource activitySource)
        : base(serviceProvider, lifetime, logger, activitySource)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, ConfigureCommandSettings settings, CancellationToken cancellationToken = default)
    {
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
            Logger.LogError(ex, "Configuration setup failed");
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
    private async Task<MigrationConfiguration> InteractiveSetupAsync(ConfigureCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var configuration = new MigrationConfiguration();

        // Step 1: Source Configuration
        console.MarkupLine("[bold blue]Step 1: Source System Configuration[/bold blue]");
        configuration.Source = await ConfigureSourceAsync(console, cancellationToken);
        console.WriteLine();

        // Step 2: Operations to configure
        console.MarkupLine("[bold blue]Step 2: Migration Operations[/bold blue]");
        var operations = await SelectOperationsAsync(console, cancellationToken);
        console.WriteLine();

        // Step 3: Configure selected operations
        if (operations.Contains("inventory"))
        {
            console.MarkupLine("[bold blue]Step 3a: Inventory Configuration[/bold blue]");
            configuration.Inventory = await ConfigureInventoryAsync(console, cancellationToken);
            console.WriteLine();
        }

        if (operations.Contains("export"))
        {
            console.MarkupLine("[bold blue]Step 3b: Export Configuration[/bold blue]");
            configuration.Export = await ConfigureExportAsync(console, cancellationToken);
            console.WriteLine();
        }

        if (operations.Contains("import"))
        {
            console.MarkupLine("[bold blue]Step 3c: Target System Configuration[/bold blue]");
            configuration.Target = await ConfigureTargetAsync(console, cancellationToken);
            console.WriteLine();
            
            console.MarkupLine("[bold blue]Step 3d: Import Configuration[/bold blue]");
            configuration.Import = await ConfigureImportAsync(console, cancellationToken);
            console.WriteLine();
        }

        // Step 4: Telemetry and Logging
        console.MarkupLine("[bold blue]Step 4: Telemetry and Logging[/bold blue]");
        configuration.Telemetry = await ConfigureTelemetryAsync(console, cancellationToken);
        
        return configuration;
    }

    /// <summary>
    /// Configures source system settings.
    /// </summary>
    private async Task<SourceConfiguration> ConfigureSourceAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var source = new SourceConfiguration();

        // Source type
        var sourceTypes = new[] { "AzureDevOpsServices", "AzureDevOpsServer", "TFS" };
        source.Type = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select source system type:")
                .AddChoices(sourceTypes));

        // Source URL
        var defaultUrl = source.Type switch
        {
            "AzureDevOpsServices" => "https://dev.azure.com/[organization]",
            "AzureDevOpsServer" or "TFS" => "http://[server]:8080/tfs/[collection]",
            _ => ""
        };

        source.Url = console.Ask<string>($"Enter source URL [{defaultUrl}]:");
        
        // Authentication
        source.Authentication = new AuthConfiguration();
        var authTypes = new[] { "PAT", "Windows" };
        source.Authentication.Type = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select authentication type:")
                .AddChoices(authTypes));

        if (source.Authentication.Type == "PAT")
        {
            source.Authentication.PersonalAccessToken = console.Prompt(
                new TextPrompt<string>("Enter Personal Access Token:")
                    .Secret());
        }

        // Project configuration
        source.Project = new ProjectConfiguration();
        var projectScope = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Project scope:")
                .AddChoices("Single Project", "Multiple Projects", "All Projects"));

        switch (projectScope)
        {
            case "Single Project":
                source.Project.Name = console.Prompt(new TextPrompt<string>("Enter project name:"));
                break;
            case "Multiple Projects":
                var projects = new List<string>();
                console.MarkupLine("Enter project names (empty line to finish):");
                while (true)
                {
                    var project = console.Prompt(
                        new TextPrompt<string>($"Project {projects.Count + 1}:")
                            .AllowEmpty());
                    if (string.IsNullOrWhiteSpace(project))
                        break;
                    projects.Add(project);
                }
                source.Project.IncludedProjects = projects;
                break;
            case "All Projects":
                source.Project.AllProjects = true;
                break;
        }

        return source;
    }

    /// <summary>
    /// Selects which operations to configure.
    /// </summary>
    private async Task<List<string>> SelectOperationsAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var operations = console.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select operations to configure:")
                .AddChoices("inventory", "export", "import")
                .InstructionsText("[grey](Press space to select, enter to confirm)[/]"));

        return operations;
    }

    /// <summary>
    /// Configures inventory operation settings.
    /// </summary>
    private async Task<InventoryConfiguration> ConfigureInventoryAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var inventory = new InventoryConfiguration();

        inventory.OutputPath = console.Prompt(
            new TextPrompt<string>("Output directory for inventory results:")
                .DefaultValue("./inventory-results"));
        inventory.IncludeWorkItems = console.Confirm("Include work items?", defaultValue: true);
        inventory.IncludeRepositories = console.Confirm("Include repositories?", defaultValue: true);
        inventory.IncludePipelines = console.Confirm("Include pipelines?", defaultValue: true);

        return inventory;
    }

    /// <summary>
    /// Configures export operation settings.
    /// </summary>
    private async Task<ExportConfiguration> ConfigureExportAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var export = new ExportConfiguration();

        export.OutputPath = console.Prompt(
            new TextPrompt<string>("Output directory for migration package:")
                .DefaultValue("./migration-package"));
        export.Compress = console.Confirm("Compress the export package?", defaultValue: true);
        export.IncludeAttachments = console.Confirm("Include work item attachments?", defaultValue: true);

        return export;
    }

    /// <summary>
    /// Configures target system settings.
    /// </summary>
    private async Task<TargetConfiguration> ConfigureTargetAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var target = new TargetConfiguration();

        target.Type = "AzureDevOpsServices"; // Most common target
        target.Url = console.Prompt(
            new TextPrompt<string>("Enter target URL (e.g., https://dev.azure.com/[organization]):"));
        
        target.Authentication = new AuthConfiguration();
        target.Authentication.Type = "PAT";
        target.Authentication.PersonalAccessToken = console.Prompt(
            new TextPrompt<string>("Enter target Personal Access Token:")
                .Secret());

        return target;
    }

    /// <summary>
    /// Configures import operation settings.
    /// </summary>
    private async Task<ImportConfiguration> ConfigureImportAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var import = new ImportConfiguration();

        import.PackagePath = console.Prompt(
            new TextPrompt<string>("Migration package path:")
                .DefaultValue("./migration-package"));
        import.ValidateFirst = console.Confirm("Validate package before import?", defaultValue: true);
        import.DryRun = console.Confirm("Start in dry-run mode?", defaultValue: false);

        return import;
    }

    /// <summary>
    /// Configures telemetry and logging settings.
    /// </summary>
    private async Task<TelemetryConfiguration> ConfigureTelemetryAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var telemetry = new TelemetryConfiguration();

        telemetry.Enabled = console.Confirm("Enable telemetry collection?", defaultValue: true);
        
        if (telemetry.Enabled)
        {
            var logLevels = new[] { "Debug", "Information", "Warning", "Error" };
            telemetry.LogLevel = console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select log level:")
                    .AddChoices(logLevels));
            
            // Set default if not specified
            if (string.IsNullOrWhiteSpace(telemetry.LogLevel))
                telemetry.LogLevel = "Information";

            telemetry.LogOutputPath = console.Prompt(
                new TextPrompt<string>("Log output directory:")
                    .DefaultValue("./logs"));
            telemetry.EnableTracing = console.Confirm("Enable OpenTelemetry tracing?", defaultValue: true);
            telemetry.EnableMetrics = console.Confirm("Enable OpenTelemetry metrics?", defaultValue: true);
        }

        return telemetry;
    }

    /// <summary>
    /// Saves the configuration to file.
    /// </summary>
    private async Task SaveConfigurationAsync(MigrationConfiguration configuration, ConfigureCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var configService = GetRequiredService<IConfigurationService>();

        var outputFile = settings.OutputFile ?? "migration.json";
        
        await configService.SaveConfigurationAsync(configuration, outputFile, cancellationToken);
        ShowSuccess(console, $"Configuration saved to: {outputFile}");
    }

    /// <summary>
    /// Shows completion summary and next steps.
    /// </summary>
    private void ShowCompletionSummary(MigrationConfiguration configuration, ConfigureCommandSettings settings)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var outputFile = settings.OutputFile ?? "migration.json";

        console.WriteLine();
        console.Write(new Rule("[bold green]Configuration Complete![/]").RuleStyle("green"));
        console.WriteLine();

        console.MarkupLine("[green]✓[/] Configuration file created successfully");
        console.MarkupLine($"[blue]📁 File:[/] {outputFile}");
        console.WriteLine();

        console.MarkupLine("[bold]Next Steps:[/]");
        
        if (configuration.Inventory != null)
        {
            console.MarkupLine($"• Run inventory: [cyan]devopsmigration discovery inventory --config {outputFile}[/]");
        }
        
        if (configuration.Export != null)
        {
            console.MarkupLine($"• Run export: [cyan]devopsmigration export --config {outputFile}[/]");
        }
        
        if (configuration.Import != null)
        {
            console.MarkupLine($"• Run import: [cyan]devopsmigration import --config {outputFile}[/]");
        }
        
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