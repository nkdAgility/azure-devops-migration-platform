// TFS Migration Agent — Worker Service (net481)
// Polls the control plane for TFS-specific jobs, executes them, and reports progress.
// Structural twin of MigrationAgent but targets net481 for TFS Object Model access.
// See docs/tfs-exporter.md.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.TfsMigrationAgent;

namespace DevOpsMigrationPlatform.TfsMigrationAgent
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // .NET Framework defaults to Hierarchical ActivityIdFormat — switch to W3C
            // so that TraceId/SpanId propagate correctly to the OTLP collector.
            System.Diagnostics.Activity.DefaultIdFormat = System.Diagnostics.ActivityIdFormat.W3C;

            var exeDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)
                         ?? AppDomain.CurrentDomain.BaseDirectory;

            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureAppConfiguration(cfgBuilder =>
            {
                cfgBuilder.SetBasePath(exeDir);
                cfgBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                cfgBuilder.AddEnvironmentVariables();
                cfgBuilder.AddCommandLine(args);
            });

            builder.ConfigureLogging((ctx, logging) =>
            {
                logging.AddOpenTelemetry(otelLogging =>
                {
                    otelLogging.IncludeFormattedMessage = true;
                    otelLogging.IncludeScopes = true;
                });
            });

            builder.UseSerilog((ctx, _, loggerConfig) =>
            {
                loggerConfig
                    .ReadFrom.Configuration(ctx.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProcessId()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information);
            });

            builder.ConfigureServices((ctx, services) =>
            {
                var controlPlaneBaseUrl = new Uri(
                    ctx.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5100");

                services.AddTfsMigrationAgentServices(ctx.Configuration, controlPlaneBaseUrl);

                // ── OpenTelemetry (inline — ServiceDefaults requires IHostApplicationBuilder) ──
                var deploymentMode = ctx.Configuration["MigrationPlatform:Environment:Type"] ?? "Standalone";
                var controlPlaneUrl = ctx.Configuration["MigrationPlatform:Environment:ControlPlane:BaseUrl"]
                                     ?? "http://localhost:5100";

                var otel = services.AddOpenTelemetry();

                otel.ConfigureResource(rb => rb.AddAttributes(
                    new Dictionary<string, object>
                    {
                        { "service.name", WellKnownServiceNames.TfsMigrationAgent },
                        { "service.namespace", WellKnownServiceNames.Namespace },
                        { WellKnownResourceAttributes.DeploymentMode, deploymentMode },
                        { WellKnownResourceAttributes.ControlPlaneUrl, controlPlaneUrl }
                    }));

                otel.WithMetrics(metrics =>
                    metrics.AddHttpClientInstrumentation()
                           .AddRuntimeInstrumentation()
                           .AddMeter(WellKnownMeterNames.Migration)
                           .AddMeter(WellKnownMeterNames.Discovery)
                           .AddMeter(WellKnownMeterNames.ControlPlane));

                otel.WithTracing(tracing =>
                    tracing.AddHttpClientInstrumentation()
                           .AddSource(WellKnownActivitySourceNames.Migration)
                           .AddSource(WellKnownActivitySourceNames.Discovery)
                           .AddSource(WellKnownActivitySourceNames.ControlPlane)
                           .AddSource(Infrastructure.TfsObjectModel.Telemetry.MigrationPlatformActivitySources.WorkItemExport.Name)
                           .AddSource(Infrastructure.TfsObjectModel.Telemetry.MigrationPlatformActivitySources.AttachmentDownload.Name));

                // OTLP export — opt-in via standard OTEL_EXPORTER_OTLP_ENDPOINT env var.
                var otlpEndpoint = ctx.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    otel.UseOtlpExporter();
                }
            });

            builder.UseConsoleLifetime(o => o.SuppressStatusMessages = true);

            var host = builder.Build();
            host.Run();
        }
    }
}
