using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Services;
using Spectre.Console.Cli;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Abstract base class providing common command infrastructure for CLI commands.
/// Provides access to DI services, lifecycle management, error handling, and telemetry.
/// Following the azure-devops-migration-tools pattern for command hosting lifecycle.
/// </summary>
/// <typeparam name="TSettings">Command settings type derived from CommandSettings</typeparam>
public abstract class CommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    /// <summary>
    /// Access to the DI container services.
    /// </summary>
    protected IServiceProvider Services { get; }
    
    /// <summary>
    /// Control over application lifecycle.
    /// </summary>
    protected IHostApplicationLifetime Lifetime { get; }
    
    /// <summary>
    /// Structured logging capability.
    /// </summary>
    protected ILogger Logger { get; }
    
    /// <summary>
    /// OpenTelemetry tracing for command operations.
    /// </summary>
    protected ActivitySource ActivitySource { get; }

    /// <summary>
    /// Initializes the command base with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">DI container for service access</param>
    /// <param name="lifetime">Application lifetime management</param>
    /// <param name="logger">Logger for the specific command type</param>
    /// <param name="activitySource">Activity source for telemetry</param>
    protected CommandBase(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime,
        ILogger logger,
        ActivitySource activitySource)
    {
        Services = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ActivitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
    }

    /// <summary>
    /// Executes the command with common error handling and telemetry.
    /// Calls ExecuteInternalAsync() for command-specific implementation.
    /// </summary>
    /// <param name="context">Command context from Spectre.Console</param>
    /// <param name="settings">Parsed command settings</param>
    /// <returns>Exit code (0 for success, non-zero for error)</returns>
    protected sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"{GetType().Name}.Execute");
        
        try
        {
            Logger.LogInformation("Starting command execution: {CommandName}", GetType().Name);
            
            // Execute command-specific logic
            var result = await ExecuteInternalAsync(context, settings, cancellationToken);
            
            if (result == 0)
            {
                Logger.LogInformation("Command completed successfully: {CommandName}", GetType().Name);
            }
            else
            {
                Logger.LogWarning("Command completed with error code {ExitCode}: {CommandName}", 
                    result, GetType().Name);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled exception in command: {CommandName}", GetType().Name);
            
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            
            return 1; // Error exit code
        }
        finally
        {
            // Ensure application lifetime is properly managed
            Lifetime.StopApplication();
        }
    }

    /// <summary>
    /// Command-specific implementation to be overridden by derived classes.
    /// Contains the actual business logic for the command.
    /// </summary>
    /// <param name="context">Command context from Spectre.Console</param>
    /// <param name="settings">Parsed command settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exit code (0 for success, non-zero for error)</returns>
    protected abstract Task<int> ExecuteInternalAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Helper method to get required services with proper error handling.
    /// </summary>
    /// <typeparam name="TService">Type of service to retrieve</typeparam>
    /// <returns>The requested service</returns>
    /// <exception cref="InvalidOperationException">When service is not registered</exception>
    protected TService GetRequiredService<TService>()
        where TService : notnull
    {
        try
        {
            return Services.GetRequiredService<TService>();
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogError("Required service {ServiceType} not registered in DI container", typeof(TService).Name);
            throw new InvalidOperationException(
                $"Required service {typeof(TService).Name} not registered in DI container. " +
                "This indicates a configuration issue in MigrationPlatformHost.", ex);
        }
    }
    
    /// <summary>
    /// Helper method to get optional services.
    /// </summary>
    /// <typeparam name="TService">Type of service to retrieve</typeparam>
    /// <returns>The requested service or null if not registered</returns>
    protected TService? GetService<TService>()
        where TService : class
    {
        return Services.GetService<TService>();
    }

    // Configuration-related helper methods

    /// <summary>
    /// Loads and validates migration configuration for command execution.
    /// </summary>
    /// <param name="settings">Command settings that may contain config file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Loaded configuration, or null if validation failed</returns>
    protected async Task<MigrationConfiguration?> LoadConfigurationAsync(TSettings settings, CancellationToken cancellationToken = default)
    {
        var configService = GetRequiredService<IConfigurationService>();
        var console = GetRequiredService<IAnsiConsole>();
        
        try
        {
            // Determine configuration file path from settings if available
            var configPath = GetConfigurationPath(settings);
            
            if (configPath != null)
            {
                ShowInfo(console, $"Loading configuration from: {configPath}");
            }
            else
            {
                var discoveredFiles = configService.DiscoverConfigurationFiles().ToList();
                if (discoveredFiles.Any())
                {
                    ShowInfo(console, $"Discovered configuration files: {string.Join(", ", discoveredFiles.Select(Path.GetFileName))}");
                }
                else
                {
                    ShowInfo(console, "No configuration file found, using default configuration");
                }
            }

            var configuration = await configService.LoadConfigurationAsync(configPath, cancellationToken);
            
            // Validate configuration if it requires validation for this command
            if (RequiresConfigurationValidation())
            {
                var validationErrors = configService.ValidateConfiguration(configuration).ToList();
                
                if (validationErrors.Any())
                {
                    ShowError(console, "Configuration validation failed:");
                    foreach (var error in validationErrors)
                    {
                        console.MarkupLine($"  [red]•[/] {error}");
                    }
                    
                    // Show helpful guidance
                    if (configPath == null)
                    {
                        ShowInfo(console, "Consider creating a migration.json configuration file with your connection settings.");
                    }
                    
                    return null;
                }
                
                ShowSuccess(console, "Configuration validated successfully");
            }
            
            return configuration;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load configuration");
            ShowError(console, $"Failed to load configuration: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the configuration file path from command settings. Override to customize how configuration path is extracted.
    /// </summary>
    /// <param name="settings">Command settings</param>
    /// <returns>Configuration file path, or null to use discovery</returns>
    protected virtual string? GetConfigurationPath(TSettings settings)
    {
        // Check if settings has a configuration path property
        return settings switch
        {
            IHasConfigFile configSettings => configSettings.ConfigFile,
            _ => null
        };
    }

    /// <summary>
    /// Determines whether this command requires configuration validation. Override to customize.
    /// </summary>
    /// <returns>True if configuration must be valid, false if command can run with minimal config</returns>
    protected virtual bool RequiresConfigurationValidation() => true;

    // UI Helper methods for consistent output formatting

    /// <summary>
    /// Creates and displays a progress task with the specified description.
    /// </summary>
    /// <param name="description">Progress task description</param>
    /// <param name="operation">Async operation to execute with progress tracking</param>
    /// <returns>Task result</returns>
    protected async Task<TResult> WithProgressAsync<TResult>(string description, Func<ProgressTask, Task<TResult>> operation)
    {
        var console = GetRequiredService<IAnsiConsole>();
        return await console.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(description);
                return await operation(task);
            });
    }

    /// <summary>
    /// Creates and displays a progress task with the specified description.
    /// </summary>
    /// <param name="description">Progress task description</param>
    /// <param name="operation">Async operation to execute with progress tracking</param>
    protected async Task WithProgressAsync(string description, Func<ProgressTask, Task> operation)
    {
        var console = GetRequiredService<IAnsiConsole>();
        await console.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(description);
                await operation(task);
            });
    }

    /// <summary>
    /// Displays a success message with consistent formatting.
    /// </summary>
    /// <param name="console">Console instance</param>
    /// <param name="message">Success message to display</param>
    protected static void ShowSuccess(IAnsiConsole console, string message)
    {
        console.MarkupLine($"[green]✓[/] {message}");
    }

    /// <summary>
    /// Displays a warning message with consistent formatting.
    /// </summary>
    /// <param name="console">Console instance</param>
    /// <param name="message">Warning message to display</param>
    protected static void ShowWarning(IAnsiConsole console, string message)
    {
        console.MarkupLine($"[yellow]⚠[/] {message}");
    }

    /// <summary>
    /// Displays an error message with consistent formatting.
    /// </summary>
    /// <param name="console">Console instance</param>
    /// <param name="message">Error message to display</param>
    protected static void ShowError(IAnsiConsole console, string message)
    {
        console.MarkupLine($"[red]✗[/] {message}");
    }

    /// <summary>
    /// Displays an informational message with consistent formatting.
    /// </summary>
    /// <param name="console">Console instance</param>
    /// <param name="message">Information message to display</param>
    protected static void ShowInfo(IAnsiConsole console, string message)
    {
        console.MarkupLine($"[blue]ℹ[/] {message}");
    }

    /// <summary>
    /// Prompts for confirmation with consistent formatting.
    /// </summary>
    /// <param name="message">Confirmation message</param>
    /// <param name="defaultValue">Default response if user just presses enter</param>
    /// <returns>True if user confirms, false otherwise</returns>
    protected bool Confirm(string message, bool defaultValue = false)
    {
        var console = GetRequiredService<IAnsiConsole>();
        return console.Confirm(message, defaultValue);
    }

    /// <summary>
    /// Displays a table with consistent formatting.
    /// </summary>
    /// <param name="title">Table title</param>
    /// <param name="configureTable">Action to configure the table</param>
    protected void ShowTable(string title, Action<Table> configureTable)
    {
        var console = GetRequiredService<IAnsiConsole>();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title(title);
            
        configureTable(table);
        console.Write(table);
    }
}

/// <summary>
/// Interface for settings that include a configuration file path.
/// </summary>
public interface IHasConfigFile
{
    /// <summary>
    /// Path to the configuration file.
    /// </summary>
    string? ConfigFile { get; }
}