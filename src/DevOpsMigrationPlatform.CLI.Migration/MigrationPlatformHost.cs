using Azure.Monitor.OpenTelemetry.Exporter;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Infrastructure;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Services;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Central host builder managing DI container and infrastructure setup for the CLI.
/// Follows the azure-devops-migration-tools pattern with configuration extraction,
/// service registration, and telemetry setup centralized away from Program.cs.
/// </summary>
public static class MigrationPlatformHost
{
    /// <summary>
    /// Creates a configured host builder with all platform services registered.
    /// Extracts --config parameter, builds layered configuration, and sets up DI container.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Configured IHostBuilder ready for command execution</returns>
    public static IHostBuilder CreateDefaultBuilder(string[] args)
    {
        // ── Step 1: Extract --config / -c from args before Spectre parses them.
        // The config file path is needed to build IConfiguration, which must happen
        // before the CommandApp (and its DI container) is created.
        var (configFile, spectreArgs) = ExtractConfigFileArg(args);

        return Host.CreateDefaultBuilder(spectreArgs)
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                // ── Step 2: Build layered IConfiguration.
                // Layer 1 — appsettings.json (bundled defaults, always present)
                // Layer 2 — migration.json / user-specified config (optional)
                // Layer 3 — environment variables (override any of the above)
                configBuilder
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddJsonFile(configFile, optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                // ── Configure logging
                services.AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Warning); // suppress noisy DI/config framework logs
                });

                // ── Platform services
                RegisterPlatformServices(services, configuration);
                
                // ── Telemetry setup
                RegisterTelemetryServices(services, configuration);
                
                // ── Inventory services
                RegisterInventoryServices(services, configuration);
                
                // ── Spectre.Console CommandApp registration
                RegisterCommandApp(services, spectreArgs);
            });
    }

    /// <summary>
    /// Registers core platform services including configuration binding and control plane client.
    /// </summary>
    private static void RegisterPlatformServices(IServiceCollection services, IConfiguration configuration)
    {
        // Registers IOptions<MigrationOptions> bound to the config root,
        // plus MigrationOptionsValidator (runs on first .Value access).
        services.AddMigrationPlatformOptions(configuration);

        // ControlPlaneClient for logs and job submission commands.
        var controlPlaneBaseUrl = configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5100";
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ControlPlaneClient>>();
            return new ControlPlaneClient(controlPlaneBaseUrl, logger);
        });
        services.AddSingleton<ILogsClient>(sp => sp.GetRequiredService<ControlPlaneClient>());
        
        // Configuration service for CLI commands
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        
        // Console service for Spectre.Console integration
        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
    }

    /// <summary>
    /// Registers OpenTelemetry tracing and metrics with Azure Monitor integration.
    /// </summary>
    private static void RegisterTelemetryServices(IServiceCollection services, IConfiguration configuration)
    {
        // ── Wire OTel SDK for CLI process
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
    /// Registers inventory-specific services for work item discovery operations.
    /// </summary>
    private static void RegisterInventoryServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IInventoryService, AzureDevOpsInventoryService>();
        services.AddOptions<InventoryOptions>().Bind(configuration);
    }

    /// <summary>
    /// Registers Spectre.Console CommandApp with DI integration and command configuration.
    /// </summary>
    private static void RegisterCommandApp(IServiceCollection services, string[] spectreArgs)
    {
        services.AddSingleton<CommandApp>(sp =>
        {
            var registrar = new TypeRegistrar(services);
            var app = new CommandApp(registrar);

            app.Configure(config =>
            {
                config.SetApplicationName("devopsmigration");
#if DEBUG
                config.PropagateExceptions();
                config.ValidateExamples();
#endif

                // Configuration command for guided setup
                config.AddCommand<ConfigureCommand>("configure")
                    .WithDescription("Interactive configuration wizard to create migration settings")
                    .WithExample("configure")
                    .WithExample("configure", "--output", "my-migration.json")
                    .WithExample("configure", "--output", "my-migration.json", "--force");

                // Discovery operations
                config.AddBranch("discovery", branch =>
                {
                    branch.SetDescription("Tools for finding out what we have and the implications of any migration");
                    
                    // Inventory command with configuration support
                    branch.AddCommand<InventoryCommand>("inventory")
                        .WithDescription("Count work items and revisions per project with enhanced configuration support")
                        .WithExample("discovery", "inventory", "--config", "migration.json")
                        .WithExample("discovery", "inventory", "--config", "migration.json", "--all-projects")
                        .WithExample("discovery", "inventory", "--source-url", "https://dev.azure.com/myorg", "--token", "***")
                        .WithExample("discovery", "inventory", "--config", "migration.json", "--output", "./custom-inventory");
                });

                // TFS Export (legacy command)
                config.AddCommand<TfsExportCommand>("tfsexport")
                    .WithDescription("Export work items from an on-premises TFS / Azure DevOps Server collection")
                    .WithExample("tfsexport",
                        "--collection", "http://tfs:8080/tfs/DefaultCollection",
                        "--project", "MyProject",
                        "--output", "./package");

                // Control plane commands
                config.AddCommand<LogsCommand>("logs")
                    .WithDescription("Retrieve or tail live ProgressEvents for a running job")
                    .WithExample("logs", "--job", "00000000-0000-0000-0000-000000000001")
                    .WithExample("logs", "--job", "00000000-0000-0000-0000-000000000001", "--follow");
            });

            return app;
        });
    }

    /// <summary>
    /// Scans <paramref name="args"/> for <c>--config</c> or <c>-c</c> and returns the
    /// resolved config file path plus a new args array with those tokens removed.
    /// If no flag is present the default <c>migration.json</c> (in the current working
    /// directory) is returned.
    /// </summary>
    private static (string configFile, string[] remainingArgs) ExtractConfigFileArg(string[] args)
    {
        var configFile = Path.Combine(Directory.GetCurrentDirectory(), "migration.json");
        var remaining = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--config" || args[i] == "-c") && i + 1 < args.Length)
            {
                configFile = args[++i]; // consume both the flag and its value
            }
            else
            {
                remaining.Add(args[i]);
            }
        }

        return (configFile, remaining.ToArray());
    }
}