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
        Action<IServiceCollection, IConfiguration>? configureServices = null,
        string? controlPlaneUrl = null)
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

                // When a resolved URL is provided (--url or MIGRATION_API_URL),
                // override the appsettings.json default so ControlPlaneOptions picks it up.
                if (!string.IsNullOrWhiteSpace(controlPlaneUrl))
                {
                    configBuilder.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("ControlPlane:BaseUrl", controlPlaneUrl)
                    ]);
                }
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
    /// Resolves the control plane URL from (in priority order):
    /// <list type="number">
    ///   <item><description><paramref name="settingsUrl"/> — the <c>--url</c> flag value parsed by Spectre.Console</description></item>
    ///   <item><description><c>MIGRATION_API_URL</c> environment variable</description></item>
    ///   <item><description><c>null</c> — signals that the CLI should start the local in-process stack</description></item>
    /// </list>
    /// </summary>
    /// <param name="settingsUrl">Value of the <c>--url</c> command option, or <c>null</c> if not supplied.</param>
    /// <returns>
    /// A non-null URL string when a remote control plane should be used, or
    /// <c>null</c> when the local <see cref="LocalStackHost"/> should be started.
    /// </returns>
    internal static string? ResolveControlPlaneUrl(string? settingsUrl)
    {
        if (!string.IsNullOrWhiteSpace(settingsUrl))
            return settingsUrl;

        var envUrl = Environment.GetEnvironmentVariable("MIGRATION_API_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
            return envUrl;

        return null;
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
                var raw = args[++i];
                configFile = Path.IsPathRooted(raw)
                    ? raw
                    : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), raw));
            }
            else
            {
                remaining.Add(args[i]);
            }
        }

        return (configFile, remaining.ToArray());
    }
}