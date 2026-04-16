using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using Microsoft.VisualStudio.Services.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Builds the DI host for the TFS export subprocess.
/// Registers all TFS-specific services plus the shared infrastructure layer.
/// </summary>
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

        public Uri TfsServer { get; }
        public string Project { get; }
        public string OutputFolder { get; }

        /// <summary>
        /// How often (by revision count) a <see cref="MetricSnapshot"/> is embedded
        /// in the yielded <see cref="WorkItemMigrationProgress.Metrics"/> field.
        /// Default: 100.
        /// </summary>
        public int SubprocessSnapshotRevisionInterval { get; set; } = 100;
    }

    public static IHostBuilder CreateDefaultBuilder(string[] args, Settings settings)
    {
        var builder = Host.CreateDefaultBuilder();
        var sessionId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var logFolder = Path.Combine(settings.OutputFolder, "logs");
        const string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        builder.UseSerilog((ctx, _, loggerConfig) =>
        {
            loggerConfig
                .ReadFrom.Configuration(ctx.Configuration)
                .Enrich.WithProperty("SessionId", sessionId)
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.File(
                    Path.Combine(logFolder, $"TfsExport-{sessionId}-errors-.log"),
                    LogEventLevel.Error, shared: true, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
                .WriteTo.File(
                    Path.Combine(logFolder, $"TfsExport-{sessionId}-.log"),
                    LogEventLevel.Verbose, outputTemplate: outputTemplate, shared: true,
                    rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Hour)
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Warning);
        });

        builder.ConfigureServices((ctx, services) =>
        {
            services.AddSingleton<IConfiguration>(ctx.Configuration);

            // Filesystem-backed artefact and state stores rooted at the output folder
            services.AddSingleton<IArtefactStore>(_ =>
                new FileSystemArtefactStore(settings.OutputFolder));
            services.AddSingleton<IStateStore>(_ =>
                new FileSystemStateStore(settings.OutputFolder));
            services.AddSingleton<ICheckpointingService, CheckpointingService>();

            // TFS connection
            services.AddSingleton<TfsTeamProjectCollection>(_ =>
            {
                var creds = new VssClientCredentials(true);
                var collection = new TfsTeamProjectCollection(settings.TfsServer, creds);
                collection.EnsureAuthenticated();
                return collection;
            });

            services.AddSingleton<WorkItemStore>(sp =>
            {
                var collection = sp.GetRequiredService<TfsTeamProjectCollection>();
                return new WorkItemStore(collection, WorkItemStoreFlags.BypassRules);
            });

            services.AddSingleton<WorkItemServer>(sp =>
            {
                var collection = sp.GetRequiredService<TfsTeamProjectCollection>();
                return collection.GetService<WorkItemServer>();
            });

            // Export services
            services.AddSingleton<IWorkItemRevisionMapper, TfsWorkItemRevisionMapper>();
            services.AddSingleton<IAttachmentDownloader, TfsAttachmentDownloader>();
            services.AddSingleton<IWorkItemExportMetrics, WorkItemExportMetrics>();
            services.AddSingleton<IAttachmentDownloadMetrics, AttachmentDownloadMetrics>();
            services.AddSingleton<TfsWorkItemQueryWindowStrategy>();
            services.AddSingleton<IWorkItemDiscoveryService, TfsObjectModelWorkItemDiscoveryService>();

            // OpenTelemetry (console exporter — configure real exporter via appsettings)
            services.AddOpenTelemetry()
                .ConfigureResource(rb =>
                {
                    rb.AddService("TfsExport");
                    rb.AddAttributes(new System.Collections.Generic.KeyValuePair<string, object>[]
                    {
                        new("session.id", sessionId),
                        new("tfs.server", settings.TfsServer.ToString()),
                        new("tfs.project", settings.Project)
                    });
                })
                .WithTracing(tb =>
                {
                    tb.AddSource(MigrationPlatformActivitySources.WorkItemExport.Name);
                    tb.AddSource(MigrationPlatformActivitySources.AttachmentDownload.Name);
                })
                .WithMetrics(mb =>
                {
                    mb.AddMeter(WorkItemExportMetrics.MeterName);
                    mb.AddMeter(AttachmentDownloadMetrics.MeterName);
                });
        });

        builder.UseConsoleLifetime(o => o.SuppressStatusMessages = true);

        builder.ConfigureAppConfiguration(cfgBuilder =>
        {
            cfgBuilder.SetBasePath(settings.OutputFolder);
            cfgBuilder.AddJsonFile(
                Path.Combine(settings.OutputFolder, "configuration.json"), optional: true);
            cfgBuilder.AddEnvironmentVariables();
            cfgBuilder.AddCommandLine(args);
        });

        return builder;
    }
}
