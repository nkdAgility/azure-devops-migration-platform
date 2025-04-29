using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using Microsoft.VisualStudio.Services.Client;
using MigrationPlatform.Abstractions.Options;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Abstractions.Telemetry;
using MigrationPlatform.Abstractions.Utilities;
using MigrationPlatform.Infrastructure.Repositories;
using MigrationPlatform.Infrastructure.Services;
using MigrationPlatform.Infrastructure.Telemetry;
using MigrationPlatform.Infrastructure.TfsObjectModel.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;

namespace MigrationPlatform.Infrastructure.TfsObjectModel
{
    public static class MigrationPlatformHost
    {

        public class Settings
        {

            public Settings(Uri tfsServer, string project, string outputFolder)
            {
                TfsServer = tfsServer;
                Project = project;
                OutputFolder = outputFolder;
            }

            public Uri TfsServer { get; set; }

            public string Project { get; set; }

            public string OutputFolder { get; set; }
        }

        public static IHostBuilder CreateDefaultBuilder(string[] args, Settings settings)
        {
            var builder = Host.CreateDefaultBuilder();

            var outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{versionString}] {Message:lj}{NewLine}{Exception}";
            var sessionId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);


            builder.UseSerilog((context, services, loggerConfiguration) =>
            {
                var LogFolder = Path.Combine(settings.OutputFolder, "logs");
                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration) // Reads Serilog config from IConfiguration
                    .Enrich.WithProperty("versionString", VersionUtilities.GetRunningVersion().versionString)
                    .Enrich.WithProperty("SessionId", sessionId)
                    .ReadFrom.Services(services) // Enables DI-based enrichment
                    .Enrich.FromLogContext()
                    .Enrich.WithProcessId()
                    .Enrich.WithSpan()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .WriteTo.File(Path.Combine(LogFolder, $"TfsExport-{sessionId}-errors-.log"), LogEventLevel.Error, shared: true, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
                    .WriteTo.File(Path.Combine(LogFolder, $"TfsExport-{sessionId}-.log"), LogEventLevel.Verbose, outputTemplate: outputTemplate, shared: true, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Hour)
                    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Warning); // Simple console sink, can expand with file, seq, etc.
            });

            builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(context.Configuration);
                services.AddSingleton<IWorkItemExportService, WorkItemExportService>();
                services.AddSingleton<IMigrationRepository, MigrationRepository>();
                services.AddSingleton<IWorkItemRevisionMapper, TfsWorkItemRevisionMapper>();
                services.AddSingleton<IAttachmentDownloader, TfsAttachmentDownloader>();
                services.AddSingleton<IAttachmentDownloadMetrics, AttachmentDownloadMetrics>();
                services.AddSingleton<IWorkItemExportMetrics, WorkItemExportMetrics>();

                services.Configure<MigrationRepositoryOptions>(context.Configuration.GetSection("MigrationRepository"));

                services.AddSingleton<TfsTeamProjectCollection>(_ =>
                {
                    var creds = new VssClientCredentials(true);
                    var collection = new TfsTeamProjectCollection(settings.TfsServer, creds);
                    collection.EnsureAuthenticated();
                    return collection;
                });

                services.AddSingleton<WorkItemStore>(provider =>
                {
                    var collection = provider.GetRequiredService<TfsTeamProjectCollection>();
                    return collection.GetService<WorkItemStore>();
                });

                services.AddSingleton<WorkItemServer>(provider =>
                {
                    var collection = provider.GetRequiredService<TfsTeamProjectCollection>();
                    return collection.GetService<WorkItemServer>();
                });

                services.PostConfigure<MigrationRepositoryOptions>(options =>
                {
                    options.RepositoryPath = settings.OutputFolder;
                });

                services.AddOpenTelemetry()
                    .ConfigureResource(builder =>
                    {
                        builder.AddService(serviceName: "TfsExportCli");
                        builder.AddAttributes(new KeyValuePair<string, object>[]
                            {
                            new("session.id", sessionId),
                            new("tfs.server", settings.TfsServer.ToString()),
                            new("tfs.project", settings.Project)
                            });
                    })
                    .WithTracing(builder =>
                    {
                        builder.AddSource(MigrationPlatformActivitySources.WorkItemExport.Name);
                        builder.AddSource(MigrationPlatformActivitySources.WorkItemImport.Name);
                        builder.AddSource(MigrationPlatformActivitySources.AttachmentDownload.Name);
                        builder.AddAzureMonitorTraceExporter(options =>
                        {
                            options.ConnectionString = MigrationPlatformActivitySources.ConnectionString;
                        }); ;
                    })
                    .WithMetrics(metricsBuilder =>
                    {
                        metricsBuilder.AddMeter(WorkItemExportMetrics.MeterName);
                        metricsBuilder.AddMeter(AttachmentDownloadMetrics.MeterName);
                        metricsBuilder.AddHttpClientInstrumentation();
                        metricsBuilder.AddRuntimeInstrumentation();
                        metricsBuilder.AddAzureMonitorMetricExporter(options =>
                        {
                            options.ConnectionString = MigrationPlatformActivitySources.ConnectionString;
                        }); ;
                    });

            });

            builder.UseConsoleLifetime(configureOptions =>
            {
                configureOptions.SuppressStatusMessages = true;
            });

            builder.ConfigureAppConfiguration(builder =>
            {
                builder.SetBasePath(settings.OutputFolder);
                builder.AddJsonFile(Path.Combine(settings.OutputFolder, "configuration.json"), optional: true);
                builder.AddEnvironmentVariables();
                builder.AddCommandLine(args);
            });
            return builder;

        }
    }
}
