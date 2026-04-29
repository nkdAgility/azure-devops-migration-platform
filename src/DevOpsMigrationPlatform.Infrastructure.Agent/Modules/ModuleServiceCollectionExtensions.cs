using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
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
        services.AddTransient<IModule, WorkItemsModule>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="InventoryDiscoveryModule"/> as an <see cref="IDiscoveryModule"/>
    /// implementation for inventory discovery operations.
    /// </summary>
    public static IServiceCollection AddInventoryDiscoveryModule(this IServiceCollection services)
    {
        services.AddSingleton<IDiscoveryModule, InventoryDiscoveryModule>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="DependencyDiscoveryModule"/> as an <see cref="IDiscoveryModule"/>
    /// implementation for dependency analysis operations.
    /// </summary>
    public static IServiceCollection AddDependencyDiscoveryModule(this IServiceCollection services)
    {
        services.AddSingleton<IDiscoveryModule, DependencyDiscoveryModule>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="NodesModule"/> as an <see cref="IModule"/> implementation
    /// for classification tree export/import operations.
    /// </summary>
    public static IServiceCollection AddNodesModule(this IServiceCollection services)
    {
        services.AddTransient<IModule, NodesModule>();
        services.AddOptions<NodesModuleOptions>();
        return services;
    }
}
