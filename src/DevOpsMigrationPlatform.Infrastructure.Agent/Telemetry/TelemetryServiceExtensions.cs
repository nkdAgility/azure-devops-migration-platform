#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Extension methods that wire up the in-process telemetry pipeline for the Migration Agent.
/// Call this from <c>Program.cs</c> after <c>AddServiceDefaults()</c>.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="TelemetryOptions"/> bound from the "Telemetry" config section.</item>
    ///   <item><see cref="IMigrationMetrics"/> as a singleton.</item>
    ///   <item><see cref="IDiscoveryMetrics"/> as a singleton.</item>
    /// </list>
    /// ControlPlane-specific metrics (IJobMetricsStore, IJobSnapshotStore, IJobLifecycleMetrics)
    /// are registered separately via <c>AddControlPlaneTelemetryServices()</c> in Infrastructure.ControlPlane.
    /// </summary>
    public static IServiceCollection AddAgentTelemetryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TelemetryOptions>()
                .BindConfiguration(TelemetryOptions.SectionName);

        services.AddSingleton<IMigrationMetrics, MigrationMetrics>();
        services.AddSingleton<IDiscoveryMetrics, DiscoveryMetrics>();

        return services;
    }

    /// <summary>
    /// Registers the in-memory <see cref="IJobMetricsStore"/> and <see cref="IJobSnapshotStore"/>
    /// implementations required by <c>ControlPlaneTelemetryTimer</c> in the Migration Agent.
    /// These stores hold the latest snapshot that the timer pushes to the Control Plane.
    /// ControlPlane-specific services (IJobLifecycleMetrics, OTel metric reader) are registered
    /// separately via <c>AddControlPlaneTelemetryServices()</c> in Infrastructure.ControlPlane.
    /// </summary>
    public static IServiceCollection AddAgentJobMetricsServices(
        this IServiceCollection services)
    {
        services.AddSingleton<IJobMetricsStore, InMemoryJobMetricsStore>();
        services.AddSingleton<IJobSnapshotStore, InMemoryJobSnapshotStore>();
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
