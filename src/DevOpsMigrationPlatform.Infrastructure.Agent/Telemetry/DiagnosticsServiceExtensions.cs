#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Extension methods that wire up the diagnostic log pipeline for the Migration Agent.
/// Registers <see cref="DiagnosticLogOptions"/>, <see cref="PackageLoggerProvider"/>,
/// <see cref="ControlPlaneLoggerProvider"/>, and adds both providers to the logging pipeline.
/// </summary>
public static class DiagnosticsServiceExtensions
{
    /// <summary>
    /// Registers the diagnostic log pipeline:
    /// <list type="bullet">
    ///   <item><see cref="DiagnosticLogOptions"/> bound from the <c>Diagnostics</c> config section.</item>
    ///   <item><see cref="PackageLoggerProvider"/> — writes NDJSON to <c>Logs/agent.jsonl</c> in the package.</item>
    ///   <item><see cref="ControlPlaneLoggerProvider"/> — pushes batches to the control plane diagnostics endpoint.</item>
    /// </list>
    /// </summary>
    public static IHostApplicationBuilder AddDiagnosticsServices(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<DiagnosticLogOptions>()
            .BindConfiguration(DiagnosticLogOptions.SectionName);

        // Package logger — writes to Logs/agent.jsonl via IArtefactStore.
        builder.Services.AddSingleton<PackageLoggerProvider>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PackageLoggerProvider>());
        builder.Logging.Services.AddSingleton<ILoggerProvider>(
            sp => sp.GetRequiredService<PackageLoggerProvider>());

        // Control plane logger — POSTs batches to /agents/lease/{leaseId}/diagnostics.
        builder.Services.AddSingleton<ControlPlaneLoggerProvider>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ControlPlaneLoggerProvider>());
        builder.Logging.Services.AddSingleton<ILoggerProvider>(
            sp => sp.GetRequiredService<ControlPlaneLoggerProvider>());

        // Named HttpClient for the diagnostics endpoint.
        builder.Services.AddHttpClient(ControlPlaneLoggerProvider.HttpClientName);

        return builder;
    }

    /// <summary>
    /// Configures the <see cref="ControlPlaneLoggerProvider"/> named <see cref="System.Net.Http.HttpClient"/>
    /// with the specified control plane base address.
    /// Call this after <see cref="AddDiagnosticsServices"/> to set the target endpoint.
    /// </summary>
    public static IServiceCollection ConfigureControlPlaneLoggerClient(
        this IServiceCollection services,
        Uri controlPlaneBaseUrl)
    {
        services.AddHttpClient(ControlPlaneLoggerProvider.HttpClientName,
            client => client.BaseAddress = controlPlaneBaseUrl);
        return services;
    }
}
#endif
