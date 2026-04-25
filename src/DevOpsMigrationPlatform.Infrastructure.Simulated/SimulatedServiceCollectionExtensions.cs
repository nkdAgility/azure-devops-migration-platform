using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Export;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated;

/// <summary>
/// Extension methods for registering Simulated connector services.
/// </summary>
public static class SimulatedServiceCollectionExtensions
{
    /// <summary>
    /// Registers Simulated export services:
    /// <list type="bullet">
    ///   <item><see cref="SimulatedEndpointOptions"/> and <see cref="SimulatedOrganisationEntry"/> for polymorphic JSON deserialization.</item>
    ///   <item><see cref="SimulatedWorkItemRevisionSourceFactory"/> as a keyed export source factory for <c>"Simulated"</c>.</item>
    ///   <item>Discovery and attachment services for Simulated sources.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddSimulatedWorkItemExport(this IServiceCollection services)
    {
        // Register endpoint and org entry types for polymorphic JSON deserialization
        services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions));
        services.AddOrganisationEntryType("Simulated", typeof(SimulatedOrganisationEntry));

        // Register revision source factory for export
        services.AddRevisionSourceFactory<SimulatedWorkItemRevisionSourceFactory>("Simulated");

        // Discovery services (for inventory of simulated sources)
        services.TryAddSingleton<SimulatedGeneratorConfig>();
        services.TryAddSingleton<IProjectDiscoveryService, SimulatedProjectDiscoveryService>();
        services.TryAddSingleton<IWorkItemDiscoveryService, SimulatedWorkItemDiscoveryService>();

        return services;
    }

    /// <summary>
    /// Registers Simulated import services:
    /// <list type="bullet">
    ///   <item><see cref="SimulatedWorkItemImportTargetFactory"/> as a keyed import target factory for <c>"Simulated"</c>.</item>
    ///   <item><see cref="SimulatedResolutionStrategyFactory"/> for <see cref="SimulatedWorkItemImportTarget"/> targets.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddSimulatedWorkItemImport(this IServiceCollection services)
    {
        // Register import target factory as keyed entry in the composite dispatcher
        services.AddImportTargetFactory<SimulatedWorkItemImportTargetFactory>("Simulated");

        // Register resolution strategy factory — always returns NullResolutionStrategy
        services.AddResolutionStrategyFactory<SimulatedResolutionStrategyFactory, SimulatedWorkItemImportTarget>();

        return services;
    }

    /// <summary>
    /// Registers Simulated dependency analysis services.
    /// The Simulated link analysis service returns empty link results.
    /// </summary>
    public static IServiceCollection AddSimulatedDependencyAnalysis(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService, SimulatedWorkItemLinkAnalysisService>(
            serviceKey: "Simulated");
        return services;
    }

    /// <summary>
    /// Registers all Simulated connector services (export, import, and dependency analysis).
    /// Convenience method that calls <see cref="AddSimulatedWorkItemExport"/>,
    /// <see cref="AddSimulatedWorkItemImport"/>, and <see cref="AddSimulatedDependencyAnalysis"/>.
    /// </summary>
    public static IServiceCollection AddSimulatedServices(this IServiceCollection services)
    {
        services.AddSimulatedWorkItemExport();
        services.AddSimulatedWorkItemImport();
        services.AddSimulatedDependencyAnalysis();
        return services;
    }
}

