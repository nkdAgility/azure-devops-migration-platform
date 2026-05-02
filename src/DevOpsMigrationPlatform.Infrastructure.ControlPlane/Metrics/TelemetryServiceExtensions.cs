// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlane.Metrics;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.ControlPlane.Metrics;

/// <summary>
/// Extension methods that wire up the ControlPlane-specific telemetry services.
/// Call this from the ControlPlane host's service configuration.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="TelemetryOptions"/> bound from the "Telemetry" config section.</item>
    ///   <item><see cref="IJobMetricsStore"/> as a singleton.</item>
    ///   <item><see cref="IJobSnapshotStore"/> as a singleton.</item>
    ///   <item><see cref="IJobLifecycleMetrics"/> as a singleton.</item>
    ///   <item>A <see cref="PeriodicExportingMetricReader"/> wrapping <see cref="SnapshotMetricExporter"/>
    ///         added to the existing OTel <see cref="MeterProviderBuilder"/>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddControlPlaneTelemetryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TelemetryOptions>()
                .BindConfiguration(TelemetryOptions.SectionName);

        services.AddSingleton<IJobMetricsStore, InMemoryJobMetricsStore>();
        services.AddSingleton<IJobSnapshotStore, InMemoryJobSnapshotStore>();
        services.AddSingleton<IJobLifecycleMetrics, JobLifecycleMetrics>();

        var options = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);
        int intervalMs = options.SnapshotIntervalSeconds * 1_000;

        services.ConfigureOpenTelemetryMeterProvider((sp, mb) =>
                {
                    mb.AddReader(sp2 =>
                    {
                        var store = sp2.GetRequiredService<IJobMetricsStore>();
                        var exporter = new SnapshotMetricExporter(store);
                        return new PeriodicExportingMetricReader(exporter, intervalMs);
                    });
                });

        return services;
    }
}
