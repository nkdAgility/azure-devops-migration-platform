using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Spectre.Console.Cli;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Abstract base class providing common command infrastructure for CLI commands.
/// Commands have parameterless constructors and create their own <see cref="IHost"/>
/// in <see cref="ExecuteInternalAsync"/> via <see cref="CreateHost"/>.
/// </summary>
/// <typeparam name="TSettings">Command settings type derived from CommandSettings</typeparam>
public abstract class CommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    /// <summary>
    /// The host created by this command. Disposed at the end of execution.
    /// Available after <see cref="CreateHost"/> is called.
    /// Tests can set this directly to inject a pre-built host.
    /// </summary>
    protected internal IHost? Host { get; internal set; }

    /// <summary>
    /// Creates an <see cref="IHost"/> using the shared <see cref="MigrationPlatformHost"/>
    /// builder with command-specific service registration.
    /// </summary>
    /// <param name="args">Original command line arguments (for config file extraction)</param>
    /// <param name="configureServices">
    /// Delegate to register command-specific services and options.
    /// </param>
    /// <returns>The built and started host. Also stored in <see cref="Host"/>.</returns>
    protected async Task<IHost> CreateHost(
        string[] args,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        if (Host is not null)
            return Host;

        Host = MigrationPlatformHost.CreateDefaultBuilder(args, configureServices).Build();
        await Host.StartAsync();
        return Host;
    }

    /// <summary>
    /// Shorthand to resolve a required service from the command's host.
    /// </summary>
    protected TService GetRequiredService<TService>() where TService : notnull
        => Host!.Services.GetRequiredService<TService>();

    /// <summary>
    /// Shorthand to resolve an optional service from the command's host.
    /// </summary>
    protected TService? GetService<TService>() where TService : class
        => Host?.Services.GetService<TService>();

    /// <summary>
    /// Executes the command with common error handling and telemetry.
    /// </summary>
    protected sealed override async Task<int> ExecuteAsync(
        CommandContext context, TSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ExecuteInternalAsync(context, settings, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unhandled exception: {Markup.Escape(ex.Message)}");
            return 1;
        }
        finally
        {
            if (Host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                Host?.Dispose();
        }
    }

    /// <summary>
    /// Command-specific implementation. Derived classes must call <see cref="CreateHost"/>
    /// to obtain an <see cref="IHost"/> with their required services.
    /// </summary>
    protected abstract Task<int> ExecuteInternalAsync(
        CommandContext context, TSettings settings, CancellationToken cancellationToken = default);

    // ── Configuration helpers ──────────────────────────────────────────────

    protected async Task<MigrationOptions?> LoadConfigurationAsync(TSettings settings, CancellationToken cancellationToken = default)
    {
        var configService = GetRequiredService<IConfigurationService>();
        var console = GetRequiredService<IAnsiConsole>();

        try
        {
            var configPath = GetConfigurationPath(settings);

            if (configPath != null)
            {
                ShowInfo(console, $"Loading configuration from: {configPath}");
            }
            else
            {
                var discoveredFiles = configService.DiscoverConfigurationFiles().ToList();
                if (discoveredFiles.Any())
                    ShowInfo(console, $"Discovered configuration files: {string.Join(", ", discoveredFiles.Select(Path.GetFileName))}");
                else
                    ShowInfo(console, "No configuration file found, using default configuration");
            }

            var options = await configService.LoadConfigurationAsync(configPath, cancellationToken);

            if (RequiresConfigurationValidation())
            {
                var validationErrors = configService.ValidateConfiguration(options).ToList();
                if (validationErrors.Any())
                {
                    ShowError(console, "Configuration validation failed:");
                    foreach (var error in validationErrors)
                        console.MarkupLine($"  [red]•[/] {error}");
                    return null;
                }
                ShowSuccess(console, "Configuration validated successfully");
            }

            return options;
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to load configuration: {ex.Message}");
            return null;
        }
    }

    protected virtual string? GetConfigurationPath(TSettings settings)
    {
        return settings switch
        {
            IHasConfigFile configSettings => configSettings.ConfigFile,
            _ => null
        };
    }

    protected virtual bool RequiresConfigurationValidation() => true;

    // ── UI helpers ─────────────────────────────────────────────────────────

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

    protected static void ShowSuccess(IAnsiConsole console, string message) =>
        console.MarkupLine($"[green]✓[/] {message}");

    protected static void ShowWarning(IAnsiConsole console, string message) =>
        console.MarkupLine($"[yellow]⚠[/] {message}");

    protected static void ShowError(IAnsiConsole console, string message) =>
        console.MarkupLine($"[red]✗[/] {message}");

    protected static void ShowInfo(IAnsiConsole console, string message) =>
        console.MarkupLine($"[blue]ℹ[/] {message}");

    protected bool Confirm(string message, bool defaultValue = false)
    {
        var console = GetRequiredService<IAnsiConsole>();
        return console.Confirm(message, defaultValue);
    }

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
    string? ConfigFile { get; }
}