using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Registers module implementations (<see cref="IModule"/> and <see cref="IDiscoveryModule"/>)
/// with the DI container. These registrations belong at the composition root — NOT inside
/// connector assemblies — so that connectors remain decoupled from module implementations.
/// </summary>
public static class ModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WorkItemsModule"/> as the <see cref="IModule"/> implementation
    /// for work item export/import operations.
    /// </summary>
    public static IServiceCollection AddWorkItemsModule(this IServiceCollection services)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<WorkItemsModuleOptions>("Work items export/import module configuration");
#endif

        services.AddTransient<IModule, WorkItemsModule>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="InventoryDiscoveryModule"/> as an <see cref="IModule"/>
    /// implementation for inventory discovery operations. Runs during the Export phase
    /// to count work items and revisions per project.
    /// </summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddTransient<IModule, InventoryDiscoveryModule>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="DependencyDiscoveryModule"/> as an <see cref="IModule"/>
    /// implementation for dependency analysis operations. Runs during the Export phase
    /// to analyze work item links across projects.
    /// </summary>
    public static IServiceCollection AddDependenciesModule(this IServiceCollection services)
    {
        services.AddTransient<IModule, DependencyDiscoveryModule>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="NodesModule"/> as an <see cref="IModule"/> implementation
    /// for classification tree export/import operations.
    /// </summary>
    public static IServiceCollection AddNodesModule(this IServiceCollection services, IConfiguration configuration)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<NodesModuleOptions>("Classification nodes (area/iteration paths) module configuration");
#endif

        services.AddTransient<IModule, NodesModule>();
        services.Configure<NodesModuleOptions>(
            configuration.GetSection(NodesModuleOptions.SectionName));
        return services;
    }
}
