using Azure.Monitor.OpenTelemetry.Exporter;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Spectre.Console;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Shared host builder for CLI commands. Each command creates its own <see cref="IHost"/>
/// by calling <see cref="CreateDefaultBuilder"/> with a delegate that registers
/// command-specific services. This keeps Program.cs free of DI and ensures that
/// ValidateOnStart only fires for the options the running command actually needs.
/// </summary>
public static class MigrationPlatformHost
{
    /// <summary>
    /// Creates a configured host builder with shared infrastructure (configuration,
    /// logging, telemetry, console) plus command-specific services registered via
    /// <paramref name="configureServices"/>.
    /// </summary>
    /// <param name="args">Command line arguments (--config is extracted for config layering)</param>
    /// <param name="configureServices">
    /// Delegate for the calling command to register its own services and options.
    /// Receives the fully-built <see cref="IConfiguration"/> so the command can bind
    /// its own options with ValidateOnStart.
    /// </param>
    /// <returns>Configured <see cref="IHostBuilder"/> ready to <c>.Build()</c></returns>
    public static IHostBuilder CreateDefaultBuilder(
        string[] args,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        var (configFile, _) = ExtractConfigFileArg(args);

        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                configBuilder
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddJsonFile(configFile, optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                // ── Shared infrastructure (every command gets these)
                services.AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Warning);
                });

                services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
                RegisterTelemetryServices(services, configuration);

                // ── Command-specific services
                configureServices?.Invoke(services, configuration);
            });
    }

    /// <summary>
    /// Registers OpenTelemetry tracing and metrics with Azure Monitor integration.
    /// </summary>
    private static void RegisterTelemetryServices(IServiceCollection services, IConfiguration configuration)
    {
        var cliSource = new ActivitySource("DevOpsMigrationPlatform.CLI");
        services.AddSingleton(cliSource);

        var telOpts = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(telOpts);

        services.AddOpenTelemetry()
            .WithTracing(b =>
            {
                b.AddSource("DevOpsMigrationPlatform.CLI")
                 .AddHttpClientInstrumentation();
                if (!string.IsNullOrEmpty(telOpts.AzureMonitorConnectionString))
                    b.AddAzureMonitorTraceExporter(o => o.ConnectionString = telOpts.AzureMonitorConnectionString);
            })
            .WithMetrics(b =>
            {
                b.AddHttpClientInstrumentation();
                if (!string.IsNullOrEmpty(telOpts.AzureMonitorConnectionString))
                    b.AddAzureMonitorMetricExporter(o => o.ConnectionString = telOpts.AzureMonitorConnectionString);
            });
    }

    /// <summary>
    /// Scans <paramref name="args"/> for <c>--config</c> or <c>-c</c> and returns the
    /// resolved config file path plus a new args array with those tokens removed.
    /// </summary>
    internal static (string configFile, string[] remainingArgs) ExtractConfigFileArg(string[] args)
    {
        var configFile = Path.Combine(Directory.GetCurrentDirectory(), "migration.json");
        var remaining = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--config" || args[i] == "-c") && i + 1 < args.Length)
            {
                configFile = args[++i];
            }
            else
            {
                remaining.Add(args[i]);
            }
        }

        return (configFile, remaining.ToArray());
    }
}