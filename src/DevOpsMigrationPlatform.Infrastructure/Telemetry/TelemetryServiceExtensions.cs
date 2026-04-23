#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Extension methods that wire up the in-process telemetry pipeline for the
/// Migration Agent and Control Plane.
/// Call this from <c>Program.cs</c> after <c>AddServiceDefaults()</c>.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="TelemetryOptions"/> bound from the "Telemetry" config section.</item>
    ///   <item><see cref="IJobMetricsStore"/> as a singleton.</item>
    ///   <item>A <see cref="PeriodicExportingMetricReader"/> wrapping <see cref="SnapshotMetricExporter"/>
    ///         added to the existing OTel <see cref="MeterProviderBuilder"/>.</item>
    /// </list>
    /// Azure Monitor and OTLP exporters are registered separately:
    /// OTLP via <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var (ServiceDefaults),
    /// Azure Monitor via <c>ServiceDefaults.AddOpenTelemetryExporters()</c>.
    /// </summary>
    public static IServiceCollection AddTelemetryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TelemetryOptions>()
                .BindConfiguration(TelemetryOptions.SectionName);

        services.AddSingleton<IJobMetricsStore, InMemoryJobMetricsStore>();
        services.AddSingleton<IJobSnapshotStore, InMemoryJobSnapshotStore>();
        services.AddSingleton<IMigrationMetrics, MigrationMetrics>();
        services.AddSingleton<IDiscoveryMetrics, DiscoveryMetrics>();

        // Add SnapshotMetricExporter to the OTel metrics pipeline via a
        // PeriodicExportingMetricReader driven by SnapshotIntervalSeconds.
        // The interval is read eagerly here; dynamic reconfiguration is out of scope.
        var options = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);
        int intervalMs = options.SnapshotIntervalSeconds * 1_000;

        services.ConfigureOpenTelemetryMeterProvider((sp, mb) =>
                {
                    // The IJobMetricsStore is resolved lazily via IServiceProvider
                    // to avoid referencing an instance before DI is fully built.
                    mb.AddReader(sp2 =>
                    {
                        var store = sp2.GetRequiredService<IJobMetricsStore>();
                        var exporter = new SnapshotMetricExporter(store);
                        return new PeriodicExportingMetricReader(exporter, intervalMs);
                    });
                });

        return services;
    }

    /// <summary>
    /// Registers the named <see cref="IControlPlaneTelemetryClient"/> / <see cref="ControlPlaneTelemetryClient"/>
    /// <see cref="System.Net.Http.HttpClient"/> with <paramref name="baseAddress"/> as the base URL.
    /// Call this from the Migration Agent's <c>Program.cs</c>.
    /// </summary>
    public static IServiceCollection AddControlPlaneTelemetryClient(
        this IServiceCollection services,
        Uri baseAddress)
    {
        services.AddHttpClient<IControlPlaneTelemetryClient, ControlPlaneTelemetryClient>(
            client => client.BaseAddress = baseAddress);
        return services;
    }

    /// <summary>
    /// Registers <see cref="ControlPlaneProgressSink"/> as a singleton and as a hosted service.
    /// The <see cref="CompositeProgressSink"/> registration is handled separately in the consuming host.
    /// </summary>
    public static IServiceCollection AddControlPlaneProgressSink(
        this IServiceCollection services,
        Uri controlPlaneBaseUrl)
    {
        services.AddHttpClient(ControlPlaneProgressSink.HttpClientName,
            client => client.BaseAddress = controlPlaneBaseUrl);

        services.AddSingleton<ControlPlaneProgressSink>();
        services.AddHostedService(sp => sp.GetRequiredService<ControlPlaneProgressSink>());

        return services;
    }
}
#endif
