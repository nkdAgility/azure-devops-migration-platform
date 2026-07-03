// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

// TFS Migration Agent — Worker Service (net481)
// Polls the control plane for TFS-specific jobs, executes them, and reports progress.
// Structural twin of MigrationAgent but targets net481 for TFS Object Model access.
// See docs/tfs-exporter.md.

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.TfsMigrationAgent;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;

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

                // Prevent customer-classified log fields from reaching the OTel/Azure Monitor pipeline.
                logging.AddDataClassificationFilter();
            });

            builder.UseSerilog((ctx, _, loggerConfig) =>
            {
                loggerConfig
                    .ReadFrom.Configuration(ctx.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProcessId()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information);

                var serilogDiagPath = ctx.Configuration["Telemetry:DiagnosticsPath"];
                if (!string.IsNullOrWhiteSpace(serilogDiagPath))
                {
                    var resolvedPath = Path.IsPathRooted(serilogDiagPath)
                        ? serilogDiagPath
                        : Path.GetFullPath(serilogDiagPath);
                    var logFile = Path.Combine(resolvedPath, $"{WellKnownServiceNames.TfsMigrationAgent}-logs.log");
                    // Serilog's File sink creates the target directory itself.
                    loggerConfig.WriteTo.File(logFile,
                        restrictedToMinimumLevel: LogEventLevel.Information,
                        outputTemplate: "[{Timestamp:O}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
                }
            });

            builder.ConfigureServices((ctx, services) =>
            {
                var controlPlaneBaseUrl = new Uri(
                    ctx.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5100");

                services.AddTfsMigrationAgentServices(ctx.Configuration, controlPlaneBaseUrl);

                services.AddAgentOtelPipeline(
                    ctx.Configuration,
                    WellKnownServiceNames.TfsMigrationAgent,
                    new[]
                    {
                        Infrastructure.TfsObjectModel.JobLifecycle.Telemetry.MigrationPlatformActivitySources.WorkItemExport.Name,
                        Infrastructure.TfsObjectModel.JobLifecycle.Telemetry.MigrationPlatformActivitySources.AttachmentDownload.Name
                    });
            });

            builder.UseConsoleLifetime(o => o.SuppressStatusMessages = true);

            var host = builder.Build();
            host.Run();
        }
    }
}
