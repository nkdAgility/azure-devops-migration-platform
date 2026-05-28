// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Identity;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Inventory;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Nodes;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Teams;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using Azure.Monitor.OpenTelemetry.Exporter;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;

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
        /// How often (by revision count) a <see cref="JobMetrics"/> is embedded
        /// in the yielded <see cref="ProgressEvent.Metrics"/> field.
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

        // .NET Framework defaults to Hierarchical ActivityIdFormat — switch to W3C
        // so that TraceId/SpanId propagate correctly to the OTLP collector.
        System.Diagnostics.Activity.DefaultIdFormat = System.Diagnostics.ActivityIdFormat.W3C;

        builder.UseSerilog((ctx, _, loggerConfig) =>
        {
            // Read OTLP endpoint from configuration (appsettings.json + env vars + CLI args).
            // Same pattern as ServiceDefaults: OTEL_EXPORTER_OTLP_ENDPOINT env var is surfaced
            // through IConfiguration by AddEnvironmentVariables().
            var otlpEndpoint = ctx.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            var hasOtlpEndpoint = !string.IsNullOrWhiteSpace(otlpEndpoint);

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

            // Forward logs to OTLP collector when endpoint is available.
            if (hasOtlpEndpoint)
            {
                loggerConfig.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint!;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["service.name"] = WellKnownServiceNames.TfsExport,
                        ["session.id"] = sessionId,
                        ["tfs.server"] = settings.TfsServer.ToString(),
                        ["tfs.project"] = settings.Project
                    };
                });
            }
        });

        builder.ConfigureServices((ctx, services) =>
        {
            services.AddSingleton<IConfiguration>(ctx.Configuration);

            // OTLP endpoint — read from configuration (appsettings.json + env vars + CLI args).
            var otlpEndpoint = ctx.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            var hasOtlpEndpoint = !string.IsNullOrWhiteSpace(otlpEndpoint);

            // Azure Monitor — opt-in via Telemetry:AzureMonitorConnectionString in appsettings.json.
            var azureMonitorConnectionString = ctx.Configuration["Telemetry:AzureMonitorConnectionString"];
            var hasAzureMonitor = !string.IsNullOrWhiteSpace(azureMonitorConnectionString);

            // Package boundary — pre-initialize ActivePackageState with the output folder
            // so that ActivePackageAccess can resolve the local root for this subprocess.
            services.AddSingleton(_ =>
            {
                var state = new ActivePackageState();
                state.CurrentJob = new Job
                {
                    JobId = "tfs-subprocess",
                    Package = new JobPackage { PackageUri = settings.OutputFolder }
                };
                return state;
            });
            services.AddPackageBoundaryServices();
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
            services.AddSingleton<ITfsAttachmentDownloader, TfsAttachmentDownloader>();
            services.AddSingleton<IWorkItemExportMetrics, WorkItemExportMetrics>();
            services.AddSingleton<IAttachmentDownloadMetrics, AttachmentDownloadMetrics>();
            services.AddSingleton<IPlatformMetrics, PlatformMetrics>();
            services.AddSingleton<TfsWorkItemQueryWindowStrategy>();
            services.AddSingleton<IWorkItemFetchService, TfsWorkItemFetchService>();
            services.AddSingleton<IWorkItemDiscoveryService, TfsObjectModelWorkItemDiscoveryService>();
            services.AddSingleton<IProjectDiscoveryService, TfsProjectDiscoveryService>();

            // Port interface wiring — TFS sources share a TfsAttachmentRegistry so that
            // attachment IDs registered during revision enumeration can be resolved during
            // binary download.  This keeps all TFS SDK types confined to the composition root.
            services.AddSingleton<TfsAttachmentRegistry>();
            var escapedProject = settings.Project.Replace("'", "''");
            var wiqlQuery = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{escapedProject}'";
            services.AddSingleton<IWorkItemRevisionSource>(sp =>
                new TfsWorkItemRevisionSource(
                    sp.GetRequiredService<WorkItemStore>(),
                    sp.GetRequiredService<IWorkItemRevisionMapper>(),
                    sp.GetRequiredService<TfsWorkItemQueryWindowStrategy>(),
                    sp.GetRequiredService<TfsAttachmentRegistry>(),
                    settings.Project,
                    wiqlQuery,
                    sp.GetRequiredService<ILogger<TfsWorkItemRevisionSource>>()));
            services.AddSingleton<IAttachmentBinarySource>(sp =>
                new TfsAttachmentBinarySource(
                    sp.GetRequiredService<ITfsAttachmentDownloader>(),
                    sp.GetRequiredService<TfsAttachmentRegistry>(),
                    sp.GetRequiredService<ILogger<TfsAttachmentBinarySource>>()));

            // Classification tree reader — reads area/iteration nodes from the TFS collection.
            services.AddSingleton<IClassificationTreeReader, TfsClassificationTreeReader>();

            // Identity and team export sources.
            services.AddSingleton<IIdentitySource, TfsIdentitySource>();
            services.AddSingleton<ITeamSource, TfsTeamSource>();

            // OpenTelemetry — metrics and traces.
            // OTLP exporter activates when OTEL_EXPORTER_OTLP_ENDPOINT is set.
            // Azure Monitor activates when Telemetry:AzureMonitorConnectionString is in appsettings.json.
            var otelBuilder = services.AddOpenTelemetry()
                .ConfigureResource(rb =>
                {
                    rb.AddService(WellKnownServiceNames.TfsExport);
                    rb.AddAttributes(new System.Collections.Generic.KeyValuePair<string, object>[]
                    {
                        new("service.namespace", WellKnownServiceNames.Namespace),
                        new(WellKnownResourceAttributes.DeploymentMode, "Standalone"),
                        new("session.id", sessionId),
                        new("tfs.server", settings.TfsServer.ToString()),
                        new("tfs.project", settings.Project)
                    });
                })
                .WithTracing(tb =>
                {
                    tb.AddSource(MigrationPlatformActivitySources.WorkItemExport.Name);
                    tb.AddSource(MigrationPlatformActivitySources.AttachmentDownload.Name);
                    if (hasOtlpEndpoint)
                        tb.AddOtlpExporter();
                    if (hasAzureMonitor)
                        tb.AddAzureMonitorTraceExporter(o => o.ConnectionString = azureMonitorConnectionString);
                })
                .WithMetrics(mb =>
                {
                    mb.AddMeter(WellKnownMeterNames.Agent);
                    if (hasOtlpEndpoint)
                        mb.AddOtlpExporter();
                    if (hasAzureMonitor)
                        mb.AddAzureMonitorMetricExporter(o => o.ConnectionString = azureMonitorConnectionString);
                });
        });

        builder.UseConsoleLifetime(o => o.SuppressStatusMessages = true);

        // Configuration: load the exe-local appsettings.json first (Telemetry settings),
        // then the package-level configuration, then env vars and CLI args.
        var exeDir = Path.GetDirectoryName(typeof(MigrationPlatformHost).Assembly.Location)
                     ?? AppDomain.CurrentDomain.BaseDirectory;
        builder.ConfigureAppConfiguration(cfgBuilder =>
        {
            cfgBuilder.SetBasePath(exeDir);
            cfgBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            cfgBuilder.AddJsonFile(
                Path.Combine(settings.OutputFolder, "configuration.json"), optional: true);
            cfgBuilder.AddEnvironmentVariables();
            cfgBuilder.AddCommandLine(args);
        });

        return builder;
    }
}
