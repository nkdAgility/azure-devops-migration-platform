using Azure.Monitor.OpenTelemetry.Exporter;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Infrastructure;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── Step 1: Extract --config / -c from args before Spectre parses them.
        // The config file path is needed to build IConfiguration, which must happen
        // before the CommandApp (and its DI container) is created.
        // Any --config / -c flag is consumed here and removed from spectreArgs so
        // Spectre does not see an undeclared option.
        var (configFile, spectreArgs) = ExtractConfigFileArg(args);

        // ── Step 2: Build layered IConfiguration.
        // Layer 1 — appsettings.json (bundled defaults, always present)
        // Layer 2 — migration.json / user-specified config (optional; commands that
        //            require it will fail validation when they access IOptions<MigrationOptions>.Value)
        // Layer 3 — environment variables (override any of the above)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(configFile, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        // ── Step 3: Build the DI container.
        // All platform services and options are registered here.
        // Module assemblies will add their own registrations when modules are introduced.
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning); // suppress noisy DI/config framework logs
        });

        services.AddSingleton<IConfiguration>(configuration);

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

        // ── Step 3b: Wire OTel SDK for CLI process (US-1)
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

        // ── Step 4: Hand the container to Spectre.Console via TypeRegistrar.
        // Commands with constructor dependencies are resolved from DI;
        // commands with no constructor fall back to Activator.CreateInstance.
        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("devopsmigration");
#if DEBUG
            config.PropagateExceptions();
            config.ValidateExamples();
#endif
            config.AddBranch("discovery", branch =>
            {
                branch.SetDescription("Tools for finding out what we have and the implications of any migration");
                branch.AddCommand<InventoryCommand>("inventory")
                    .WithDescription("Discover the contents of your Azure DevOps organisation")
                    .WithExample("discovery", "inventory", "--organisation", "https://dev.azure.com/myorg", "--token", "<pat>");
            });

            config.AddCommand<TfsExportCommand>("tfsexport")
                .WithDescription("Export work items from an on-premises TFS / Azure DevOps Server collection")
                .WithExample("tfsexport",
                    "--collection", "http://tfs:8080/tfs/DefaultCollection",
                    "--project", "MyProject",
                    "--output", "./package");

            config.AddCommand<LogsCommand>("logs")
                .WithDescription("Retrieve or tail live ProgressEvents for a running job")
                .WithExample("logs", "--job", "00000000-0000-0000-0000-000000000001")
                .WithExample("logs", "--job", "00000000-0000-0000-0000-000000000001", "--follow");
        });

        try
        {
            AnsiConsole.Write(new FigletText("DevOps Migration").LeftJustified().Color(Color.Blue));
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
            var result = await app.RunAsync(spectreArgs);

            var sp = registrar.BuiltServiceProvider;
            if (sp?.GetService<TracerProvider>() is { } tp)
            {
                tp.ForceFlush(5000);
                tp.Dispose();
            }
            if (sp?.GetService<MeterProvider>() is { } mp)
            {
                mp.ForceFlush(5000);
                mp.Dispose();
            }

            return result;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]❌ Unhandled exception during CLI execution[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
            return 1;
        }
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
