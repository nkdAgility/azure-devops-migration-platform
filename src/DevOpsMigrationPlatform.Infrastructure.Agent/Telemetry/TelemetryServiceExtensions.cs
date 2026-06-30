// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    ///   <item><see cref="IPlatformMetrics"/> as a singleton.</item>
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

        services.AddSingleton<IPlatformMetrics, PlatformMetrics>();

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

    /// <summary>
    /// Registers <see cref="UnifiedWorkerEventWriter"/> as a singleton, hosted service, and
    /// <see cref="IProgressSink"/> implementation.  Replaces the separate
    /// <see cref="ControlPlaneProgressSink"/> registration for agents that opt into Phase C.
    /// </summary>
    public static IServiceCollection AddUnifiedWorkerEventWriter(
        this IServiceCollection services,
        Uri controlPlaneBaseUrl)
    {
        services.AddHttpClient(UnifiedWorkerEventWriter.HttpClientName,
            client => client.BaseAddress = controlPlaneBaseUrl);

        services.AddSingleton<UnifiedWorkerEventWriter>();
        services.AddHostedService(sp => sp.GetRequiredService<UnifiedWorkerEventWriter>());
        services.AddSingleton<IProgressSink>(sp => sp.GetRequiredService<UnifiedWorkerEventWriter>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="AnsiProgressSink"/> and the <see cref="CompositeProgressSink"/>
    /// that fans every <see cref="ProgressEvent"/> out to <see cref="AnsiProgressSink"/>,
    /// <see cref="PackageProgressSink"/>, and <see cref="UnifiedWorkerEventWriter"/>.
    /// Requires <see cref="PackageProgressSink"/> and <see cref="UnifiedWorkerEventWriter"/>
    /// to already be registered (e.g. via <see cref="AddUnifiedWorkerEventWriter"/>).
    /// </summary>
    public static IServiceCollection AddCompositeProgressSink(
        this IServiceCollection services)
    {
        services.AddSingleton<AnsiProgressSink>();
        services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(
            sp.GetRequiredService<ILogger<CompositeProgressSink>>(),
            sp.GetRequiredService<AnsiProgressSink>(),
            sp.GetRequiredService<PackageProgressSink>(),
            sp.GetRequiredService<UnifiedWorkerEventWriter>()));
        return services;
    }
}
