// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Registers the shared core services required by every Migration Agent process
/// (both the net10.0 <c>MigrationAgent</c> and the net481 <c>TfsMigrationAgent</c>).
/// Call this from each agent's specific service-registration extension before
/// registering connector-specific modules and workers.
/// </summary>
public static class CoreAgentServiceExtensions
{
    /// <summary>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="ActiveLeaseState"/> and <see cref="ActivePackageState"/> ambient singletons.</item>
    ///   <item>Agent telemetry — <c>IPlatformMetrics</c>, <c>IPlatformMetrics</c>, job metrics stores.</item>
    ///   <item>Named <c>"ControlPlane"</c> <see cref="System.Net.Http.HttpClient"/> (optionally configured via <paramref name="configureControlPlane"/>).</item>
    ///   <item><see cref="ControlPlaneProgressSink"/>, <see cref="PackageProgressSink"/>, and <see cref="CompositeProgressSink"/> as <c>IProgressSink</c>.</item>
    ///   <item><see cref="IPhaseTrackingServiceFactory"/>, <see cref="IPackageStoreFactory"/>, <see cref="ICheckpointingServiceFactory"/>.</item>
    ///   <item>Diagnostic log pipeline (<see cref="DiagnosticsServiceExtensions.AddDiagnosticsServices(IServiceCollection, Uri)"/>).</item>
    ///   <item><see cref="ControlPlaneTelemetryTimer"/> background service.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">The host configuration (used by <c>AddAgentTelemetryServices</c>).</param>
    /// <param name="controlPlaneBaseUrl">The base URL of the running ControlPlane API.</param>
    /// <param name="configureControlPlane">
    /// Optional callback to further configure the <c>"ControlPlane"</c> <see cref="IHttpClientBuilder"/>
    /// (e.g. add Polly resilience handlers on net10.0). Pass <see langword="null"/> to use defaults.
    /// </param>
    public static IServiceCollection AddCoreAgentServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri controlPlaneBaseUrl,
        Action<IHttpClientBuilder>? configureControlPlane = null)
    {
        services
            .AddAgentLifecycleStateServices()
            .AddExecutionPlanningServices()
            .AddAgentTelemetryAndDiagnostics(configuration, controlPlaneBaseUrl)
            .AddControlPlaneIntegration(controlPlaneBaseUrl, configureControlPlane)
            .AddPackageExecutionServices();

        return services;
    }

    private static IServiceCollection AddAgentLifecycleStateServices(this IServiceCollection services)
    {
        services.AddSingleton<ActiveLeaseState>();
        services.AddSingleton<ActivePackageState>();
        services.AddSingleton<ICurrentPackageConfigAccessor, CurrentPackageConfigAccessor>();
        services.AddSingleton<ICurrentAgentJobContextAccessor, CurrentAgentJobContextAccessor>();
        services.AddSingleton<ICurrentJobEndpointAccessor, CurrentJobEndpointAccessor>();
        services.AddSingleton<IActiveJobState, ActiveJobState>();
        return services;
    }

    private static IServiceCollection AddExecutionPlanningServices(this IServiceCollection services)
    {
        services.AddSingleton<ProcessingCadencePolicy>();
        services.AddScoped<IJobExecutionPlanBuilder, JobExecutionPlanBuilder>();
        services.AddScoped<IJobPlanExecutor, JobPlanExecutor>();
        return services;
    }

    private static IServiceCollection AddAgentTelemetryAndDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri controlPlaneBaseUrl)
    {
        services.AddAgentTelemetryServices(configuration);
        services.AddAgentJobMetricsServices();
        services.AddDiagnosticsServices(controlPlaneBaseUrl);
        services.AddSingleton<PackageProgressSink>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PackageProgressSink>());
        services.AddSingleton<IFlushable>(sp => sp.GetRequiredService<PackageProgressSink>());
        services.AddCompositeProgressSink();
        services.AddSingleton<IHostedService, ControlPlaneTelemetryTimer>();
        return services;
    }

    private static IServiceCollection AddControlPlaneIntegration(
        this IServiceCollection services,
        Uri controlPlaneBaseUrl,
        Action<IHttpClientBuilder>? configureControlPlane)
    {
        services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);

        var controlPlaneHttpBuilder = services.AddHttpClient(
            "ControlPlane",
            client => client.BaseAddress = controlPlaneBaseUrl);
        configureControlPlane?.Invoke(controlPlaneHttpBuilder);

        services.AddSingleton<AgentControlPlaneClientAdapter>();
        services.AddSingleton<IControlPlaneAgentClient>(sp =>
            sp.GetRequiredService<AgentControlPlaneClientAdapter>());
        services.AddControlPlaneProgressSink(controlPlaneBaseUrl);
        return services;
    }

    private static IServiceCollection AddPackageExecutionServices(this IServiceCollection services)
    {
        services.AddSingleton<IPhaseTrackingServiceFactory, PhaseTrackingServiceFactory>();
        services.AddPackageManagementServices();
        services.AddSingleton<ICheckpointingServiceFactory, CheckpointingServiceFactory>();
        return services;
    }
}
