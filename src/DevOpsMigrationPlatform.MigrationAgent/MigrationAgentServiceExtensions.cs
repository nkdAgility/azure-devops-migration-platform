using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.Factories;
using DevOpsMigrationPlatform.Infrastructure.JobEngine;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
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
        // Register snapshot exporter + IMetricSnapshotStore + TelemetryOptions.
        builder.Services.AddTelemetryServices(builder.Configuration);

        // Register WellKnownMeterNames meters in the OTel pipeline.
        // Do NOT reference WorkItemExportMetrics.MeterName (lives in the .NET 4.8 assembly).
        builder.Services.AddOpenTelemetry()
            .WithMetrics(mb => mb
                .AddMeter(WellKnownMeterNames.WorkItemExport)
                .AddMeter(WellKnownMeterNames.AttachmentDownload));

        // Singleton to carry the current lease id across services.
        builder.Services.AddSingleton<ActiveLeaseState>();

        // Singleton to carry the current job's artefact store across services.
        // Set by MigrationAgentWorker when a lease is acquired, cleared on release.
        builder.Services.AddSingleton<ActivePackageState>();

        // Named HttpClient for the Control Plane telemetry push.
        builder.Services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);

        // Named HttpClient used by MigrationAgentWorker to poll /agents/lease and signal completion.
        builder.Services.AddHttpClient("ControlPlane",
            client => client.BaseAddress = controlPlaneBaseUrl);

        // Progress streaming to the Control Plane ring buffer.
        builder.Services.AddControlPlaneProgressSink(controlPlaneBaseUrl);

        // Register IModule implementations (WorkItemsModule + Azure DevOps infra).
        builder.Services.AddAzureDevOpsWorkItemExport();
        builder.Services.AddAzureDevOpsWorkItemImport();

        // Register IDiscoveryModule implementations for DiscoveryAgentWorker.
        builder.Services.AddAzureDevOpsInventory(builder.Configuration);
        builder.Services.AddAzureDevOpsDependencyAnalysis(builder.Configuration);

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
        builder.Services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(
            sp.GetRequiredService<ILogger<CompositeProgressSink>>(),
            new AnsiProgressSink(),
            sp.GetRequiredService<PackageProgressSink>(),
            sp.GetRequiredService<ControlPlaneProgressSink>()));

        // Background timer that pushes MetricSnapshots to the Control Plane.
        builder.Services.AddHostedService<ControlPlaneTelemetryTimer>();

        // Unified worker — polls /agents/lease and dispatches to migration or discovery execution.
        builder.Services.AddHostedService<JobAgentWorker>();

        return builder;
    }
}
