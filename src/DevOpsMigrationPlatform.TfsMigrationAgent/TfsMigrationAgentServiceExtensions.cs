using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;

namespace DevOpsMigrationPlatform.TfsMigrationAgent;

/// <summary>
/// Registers all TFS Migration Agent services into the host's DI container.
/// Structural twin of <c>MigrationAgentServiceExtensions</c> but targets net481
/// and uses <see cref="IServiceCollection"/> directly (no <c>IHostApplicationBuilder</c>
/// on net481).
/// </summary>
public static class TfsMigrationAgentServiceExtensions
{
    public static IServiceCollection AddTfsMigrationAgentServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri controlPlaneBaseUrl)
    {
        // Singleton to carry the current lease id across services.
        services.AddSingleton<ActiveLeaseState>();

        // Singleton to carry the current job's artefact store across services.
        services.AddSingleton<ActivePackageState>();

        // Agent telemetry (IMigrationMetrics, IDiscoveryMetrics, TelemetryOptions).
        services.AddAgentTelemetryServices(configuration);

        // In-memory stores for job metrics and snapshots (IJobMetricsStore, IJobSnapshotStore).
        services.AddAgentJobMetricsServices();

        // Named HttpClient for the Control Plane telemetry push.
        services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);

        // Named HttpClient used by TfsJobAgentWorker to poll /agents/lease and signal completion.
        // No AddStandardResilienceHandler on net481 — simple retry in the polling loop is sufficient
        // for localhost communication.
        services.AddHttpClient("ControlPlane",
            client => client.BaseAddress = controlPlaneBaseUrl);

        // Progress streaming to the Control Plane ring buffer.
        services.AddControlPlaneProgressSink(controlPlaneBaseUrl);

        // Phase tracking factory for Both mode (export + import phases).
        services.AddSingleton<IPhaseTrackingServiceFactory, PhaseTrackingServiceFactory>();

        // Package store factory — resolves file:/// URIs to FileSystem stores.
        services.AddSingleton<IPackageStoreFactory, FileSystemPackageStoreFactory>();

        // Checkpointing factory for per-job cursor management.
        services.AddSingleton<ICheckpointingServiceFactory, CheckpointingServiceFactory>();

        // Diagnostic log pipeline — register inline since AddDiagnosticsServices requires
        // IHostApplicationBuilder (not available on net481 Host.CreateDefaultBuilder path).
        services.AddOptions<DiagnosticLogOptions>()
            .BindConfiguration(DiagnosticLogOptions.SectionName);
        services.AddSingleton<PackageLoggerProvider>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PackageLoggerProvider>());
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<PackageLoggerProvider>());
        services.AddSingleton<ControlPlaneLoggerProvider>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ControlPlaneLoggerProvider>());
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<ControlPlaneLoggerProvider>());
        services.ConfigureControlPlaneLoggerClient(controlPlaneBaseUrl);

        // Package progress persistence — writes ProgressEvent NDJSON to Logs/progress.jsonl.
        services.AddSingleton<PackageProgressSink>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PackageProgressSink>());

        // Composite sink fans out every ProgressEvent to all three sinks.
        services.AddSingleton<AnsiProgressSink>();
        services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(
            sp.GetRequiredService<ILogger<CompositeProgressSink>>(),
            sp.GetRequiredService<AnsiProgressSink>(),
            sp.GetRequiredService<PackageProgressSink>(),
            sp.GetRequiredService<ControlPlaneProgressSink>()));

        // Background timer that pushes JobMetrics to the Control Plane.
        services.AddSingleton<IHostedService, ControlPlaneTelemetryTimer>();

        // Unified worker — polls /agents/lease?capabilities=tfs and dispatches to TFS execution.
        services.AddSingleton<IHostedService, TfsJobAgentWorker>();

        return services;
    }
}
