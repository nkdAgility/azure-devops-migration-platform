using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Shared observability, resilience, and service discovery defaults used by the
/// Control Plane and Migration Agent.  Based on the Aspire ServiceDefaults pattern.
/// See docs/aspire-integration.md.
/// </summary>
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder, string? serviceName = null)
    {
        builder.ConfigureOpenTelemetry(serviceName);

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder, string? serviceName = null)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry();

        // Read deployment context from the host's configuration so Application Insights
        // can distinguish Standalone vs Hosted runs and correlate with a control plane.
        var envSection = builder.Configuration.GetSection("MigrationPlatform:Environment");
        var deploymentMode = envSection["Type"] ?? "Standalone";
        var controlPlaneUrl = envSection["ControlPlane:BaseUrl"] ?? "http://localhost:5100";

        if (!string.IsNullOrEmpty(serviceName))
        {
            otel.ConfigureResource(rb => rb.AddAttributes(
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { "service.name", serviceName },
                    { "service.namespace", DevOpsMigrationPlatform.Abstractions.WellKnownServiceNames.Namespace },
                    { DevOpsMigrationPlatform.Abstractions.WellKnownResourceAttributes.DeploymentMode, deploymentMode },
                    { DevOpsMigrationPlatform.Abstractions.WellKnownResourceAttributes.ControlPlaneUrl, controlPlaneUrl }
                }));
        }

        otel.WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation()
                       // Subscribe to platform custom meters so Azure Monitor exports them.
                       // These meters are defined in Infrastructure/Telemetry/ and recorded
                       // by the Migration Agent during job execution.
                       .AddMeter(DevOpsMigrationPlatform.Abstractions.WellKnownMeterNames.Migration)
                       .AddMeter(DevOpsMigrationPlatform.Abstractions.WellKnownMeterNames.Discovery)
                       .AddMeter(DevOpsMigrationPlatform.Abstractions.WellKnownMeterNames.ControlPlane);
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        // OTLP export is configured exclusively via the standard env var.
        // Do NOT duplicate this via TelemetryOptions — it would register two OTLP exporters.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlpExporter)
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        // Azure Monitor — opt-in via Telemetry:AzureMonitorConnectionString in appsettings.
        var azureMonitorConnectionString = builder.Configuration["Telemetry:AzureMonitorConnectionString"];
        if (!string.IsNullOrWhiteSpace(azureMonitorConnectionString))
            builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
                o.ConnectionString = azureMonitorConnectionString);

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
