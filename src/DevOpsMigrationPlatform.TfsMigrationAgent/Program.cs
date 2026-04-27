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
            });

            builder.UseConsoleLifetime(o => o.SuppressStatusMessages = true);

            var host = builder.Build();
            host.Run();
        }
    }
}
