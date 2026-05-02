using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
#endif
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
    /// Convenience method that registers all agent modules and their orchestrators.
    /// Calls <see cref="AddWorkItemsModule"/>, <see cref="AddInventoryModule"/>,
    /// <see cref="AddDependenciesModule"/>, <see cref="AddNodesModule"/>,
    /// <see cref="AddIdentitiesModule"/>, and (on net10.0+) <see cref="TeamsServiceCollectionExtensions.AddTeamsModule"/>.
    /// </summary>
    public static IServiceCollection AddAllAgentModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddWorkItemsModule();
        services.AddInventoryModule();
        services.AddDependenciesModule();
        services.AddNodesModule(configuration);
        services.AddIdentitiesModule(configuration);
#if !NET481
        services.AddTeamsModule(configuration);
#endif
        return services;
    }

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
    /// Registers <see cref="InventoryModule"/> and <see cref="InventoryDiscoveryModule"/>
    /// as <see cref="IModule"/> implementations. <c>InventoryModule</c> handles single-source
    /// inventory during Export/Migrate (pulled by WorkItems dependency). <c>InventoryDiscoveryModule</c>
    /// handles standalone multi-org inventory discovery jobs.
    /// </summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddSingleton<IInventoryOrchestrator, InventoryOrchestrator>();
        services.AddTransient<IModule, InventoryModule>();
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
        services.AddSingleton<IDependencyOrchestrator, DependencyOrchestrator>();
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

        services.AddScoped<INodesOrchestrator, NodesOrchestrator>();
        services.AddTransient<IModule, NodesModule>();
        services.Configure<NodesModuleOptions>(
            configuration.GetSection(NodesModuleOptions.SectionName));
        return services;
    }
}
