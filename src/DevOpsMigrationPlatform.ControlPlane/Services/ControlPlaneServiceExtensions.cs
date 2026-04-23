using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Factories;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// Registers all ControlPlane services into the host's DI container.
/// Called from ControlPlaneHost/Program.cs (and unit test hosts).
/// </summary>
public static class ControlPlaneServiceExtensions
{
    public static IServiceCollection AddControlPlaneServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register snapshot exporter + IJobMetricsStore + TelemetryOptions.
        services.AddTelemetryServices(configuration);

        // Job queue — holds all submitted Jobs (MigrationJob and DiscoveryJob) until an agent acquires a lease.
        services.AddSingleton<IJobStore, JobStore>();

        // Lease–job mapping (stub; replace with durable EF Core store in a later phase).
        services.AddSingleton<ILeaseJobResolver, StubLeaseJobResolver>();

        // In-memory telemetry snapshot store for push (POST) and pull (GET).
        services.AddSingleton<JobTelemetryStore>();

        // In-memory job snapshot store (Channel 3) for push (POST) and pull (GET).
        services.AddSingleton<JobSnapshotStore>();

        // In-memory progress event ring buffer store.
        services.AddSingleton<JobProgressStore>();
        services.AddOptions<JobProgressOptions>()
                .BindConfiguration(JobProgressOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

        // In-memory diagnostic log ring buffer store.
        services.AddSingleton<DiagnosticLogStore>();
        services.AddOptions<DiagnosticLogStoreOptions>()
                .BindConfiguration(DiagnosticLogStoreOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

        // Package store factory — resolves file:/// URIs to FileSystem stores.
        // Used by LogDownloadController to read log files from completed job packages.
        services.AddSingleton<IPackageStoreFactory, FileSystemPackageStoreFactory>();

        return services;
    }
}
