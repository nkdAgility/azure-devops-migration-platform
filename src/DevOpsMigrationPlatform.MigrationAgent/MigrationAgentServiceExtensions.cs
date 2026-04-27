using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Registers all Migration Agent services into a host's DI container.
/// Called from <c>Program.cs</c> (standalone agent) and from <c>LocalStackHost</c>
/// (CLI in-process local stack). Both paths use the exact same registrations.
/// </summary>
public static class MigrationAgentServiceExtensions
{
    /// <summary>
    /// Registers all services required by <see cref="MigrationAgentWorker"/> and its
    /// dependencies. Does not call <c>AddServiceDefaults()</c> — that is Aspire-specific
    /// and must only be called from the standalone <c>Program.cs</c>.
    /// </summary>
    /// <param name="builder">The application host builder.</param>
    /// <param name="controlPlaneBaseUrl">The base URL of the running ControlPlane API.</param>
    public static IHostApplicationBuilder AddMigrationAgentServices(
        this IHostApplicationBuilder builder,
        Uri controlPlaneBaseUrl)
    {
        // Register agent-specific telemetry (IMigrationMetrics, IDiscoveryMetrics, TelemetryOptions).
        builder.Services.AddAgentTelemetryServices(builder.Configuration);

        // Register in-memory stores for job metrics and snapshots (IJobMetricsStore, IJobSnapshotStore).
        builder.Services.AddAgentJobMetricsServices();

        // Register WellKnownMeterNames meters in the OTel pipeline.
        // Use ConfigureOpenTelemetryMeterProvider (the pattern recommended by the
        // Azure Monitor docs) so the subscription runs on the same MeterProvider
        // that UseAzureMonitor() exports from.
        builder.Services.ConfigureOpenTelemetryMeterProvider((sp, mb) => mb
                .AddMeter(WellKnownMeterNames.Migration)
                .AddMeter(WellKnownMeterNames.Discovery));

        // Singleton to carry the current lease id across services.
        builder.Services.AddSingleton<ActiveLeaseState>();

        // Singleton to carry the current job's artefact store across services.
        // Set by MigrationAgentWorker when a lease is acquired, cleared on release.
        builder.Services.AddSingleton<ActivePackageState>();

        // Named HttpClient for the Control Plane telemetry push.
        builder.Services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);

        // Named HttpClient used by MigrationAgentWorker to poll /agents/lease and signal completion.
        // The /agents/lease endpoint is a long-poll held open by the server for ~10s.
        // Increase timeouts to prevent Polly from firing spurious timeouts on every idle poll.
        // Polly validation constraints:
        //   TotalRequestTimeout >= AttemptTimeout * (MaxRetries + 1)  →  150s >= 30s * 4 = 120s ✓
        //   CircuitBreaker.SamplingDuration >= 2 * AttemptTimeout     →  61s  >= 2 * 30s = 60s  ✓
        builder.Services.AddHttpClient("ControlPlane",
            client => client.BaseAddress = controlPlaneBaseUrl)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(150);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(61);
            });

        // Progress streaming to the Control Plane ring buffer.
        builder.Services.AddControlPlaneProgressSink(controlPlaneBaseUrl);

        // Register cross-cutting tool services (NodeStructure + FieldTransform).
        builder.Services.AddNodeStructureToolServices();
        builder.Services.AddFieldTransformToolServices();

        // Register IModule implementations (WorkItemsModule + Azure DevOps infra).
        builder.Services.AddAzureDevOpsWorkItemExport();
        builder.Services.AddAzureDevOpsWorkItemImport();
        builder.Services.AddWorkItemsModule();

        // Simulated connector — required for offline tests and CI scenarios.
        builder.Services.AddSimulatedServices();

        // Register IDiscoveryModule implementations for DiscoveryAgentWorker.
        builder.Services.AddAzureDevOpsInventory(builder.Configuration);
        builder.Services.AddAzureDevOpsDependencyAnalysis(builder.Configuration);
        builder.Services.AddInventoryDiscoveryModule();
        builder.Services.AddDependencyDiscoveryModule();

        // Phase tracking factory for MigrationAgentWorker (per-job IStateStore wiring).
        builder.Services.AddSingleton<IPhaseTrackingServiceFactory, PhaseTrackingServiceFactory>();

        // Package store factory — resolves file:/// URIs to FileSystem stores.
        builder.Services.AddSingleton<IPackageStoreFactory, FileSystemPackageStoreFactory>();

        // Diagnostic log pipeline — writes ILogger output to Logs/agent.jsonl in the package
        // and POSTs batches to the control plane diagnostics endpoint.
        builder.AddDiagnosticsServices();
        builder.Services.ConfigureControlPlaneLoggerClient(controlPlaneBaseUrl);

        // Package progress persistence — writes ProgressEvent NDJSON to Logs/progress.jsonl.
        builder.Services.AddSingleton<PackageProgressSink>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PackageProgressSink>());

        // Composite sink fans out every ProgressEvent to all three sinks.
        builder.Services.AddSingleton<AnsiProgressSink>();
        builder.Services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(
            sp.GetRequiredService<ILogger<CompositeProgressSink>>(),
            sp.GetRequiredService<AnsiProgressSink>(),
            sp.GetRequiredService<PackageProgressSink>(),
            sp.GetRequiredService<ControlPlaneProgressSink>()));

        // Background timer that pushes JobMetrics to the Control Plane.
        builder.Services.AddHostedService<ControlPlaneTelemetryTimer>();

        // Unified worker — polls /agents/lease and dispatches to migration or discovery execution.
        builder.Services.AddHostedService<JobAgentWorker>();

        return builder;
    }
}
