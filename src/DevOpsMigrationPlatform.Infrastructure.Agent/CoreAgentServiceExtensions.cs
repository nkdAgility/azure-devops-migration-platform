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
        // Ambient singletons shared across services within a lease/job lifecycle.
        services.AddSingleton<ActiveLeaseState>();
        services.AddSingleton<ActivePackageState>();
        services.AddSingleton<IJobConfiguration, JobConfiguration>();
        services.AddSingleton<ICurrentPackageConfigAccessor, CurrentPackageConfigAccessor>();
        services.AddSingleton<ICurrentAgentJobContextAccessor, CurrentAgentJobContextAccessor>();
        services.AddSingleton<ICurrentJobEndpointAccessor, CurrentJobEndpointAccessor>();
        services.AddSingleton<IActiveJobState, ActiveJobState>();

        // Execution plan builder — builds the ordered task list at job start.
        services.AddSingleton<IJobExecutionPlanBuilder, JobExecutionPlanBuilder>();

        // Execution plan executor — runs tasks in dependency-tier order with Task.WhenAll per tier.
        services.AddSingleton<IJobPlanExecutor, JobPlanExecutor>();

        // Agent telemetry (IPlatformMetrics, IPlatformMetrics, TelemetryOptions).
        services.AddAgentTelemetryServices(configuration);

        // In-memory stores for job metrics and snapshots (IJobMetricsStore, IJobSnapshotStore).
        services.AddAgentJobMetricsServices();

        // Named HttpClient for the Control Plane telemetry push.
        services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);

        // Named HttpClient used by the agent worker to poll /agents/lease and signal completion.
        // The caller may inject Polly resilience handlers via configureControlPlane (net10.0 only).
        var controlPlaneHttpBuilder = services.AddHttpClient("ControlPlane",
            client => client.BaseAddress = controlPlaneBaseUrl);
        configureControlPlane?.Invoke(controlPlaneHttpBuilder);

        // Agent-side control-plane client — used for stale-lock detection by PackageLockFileService.
        services.AddSingleton<AgentControlPlaneClientAdapter>();
        services.AddSingleton<IControlPlaneAgentClient>(sp =>
            sp.GetRequiredService<AgentControlPlaneClientAdapter>());

        // Progress streaming to the Control Plane ring buffer.
        services.AddControlPlaneProgressSink(controlPlaneBaseUrl);

        // Phase tracking factory for Both mode (export + import phases).
        services.AddSingleton<IPhaseTrackingServiceFactory, PhaseTrackingServiceFactory>();

        // Package management — store factory, preparer, and config store.
        services.AddPackageManagementServices();

        // Checkpointing factory for per-job cursor management.
        services.AddSingleton<ICheckpointingServiceFactory, CheckpointingServiceFactory>();

        // Diagnostic log pipeline (writes to Logs/agent.jsonl and pushes to control plane).
        services.AddDiagnosticsServices(controlPlaneBaseUrl);

        // Package progress persistence — writes ProgressEvent NDJSON to Logs/progress.jsonl.
        services.AddSingleton<PackageProgressSink>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PackageProgressSink>());
        services.AddSingleton<IFlushable>(sp => sp.GetRequiredService<PackageProgressSink>());

        // Composite sink fans out every ProgressEvent to all three sinks.
        services.AddCompositeProgressSink();

        // Background timer that pushes JobMetrics to the Control Plane.
        services.AddSingleton<IHostedService, ControlPlaneTelemetryTimer>();

        return services;
    }
}
