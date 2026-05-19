// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// <see cref="AddIdentitiesModule"/>, and <see cref="TeamsServiceCollectionExtensions.AddTeamsModule"/>.
    /// </summary>
    public static IServiceCollection AddAllAgentModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddWorkItemsModule(configuration);
        services.AddInventoryOrchestratorServices();
        services.AddInventoryAnalyserServices();
        services.AddDependencyAnalyserServices();
        services.AddNodesModule(configuration);
        services.AddIdentitiesModule(configuration);
        services.AddTeamsModule(configuration);
        return services;
    }

    /// <summary>
    /// Registers <see cref="WorkItemsModule"/> as the <see cref="IModule"/> implementation
    /// for work item export/import operations.
    /// </summary>
    public static IServiceCollection AddWorkItemsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<WorkItemsModuleOptions>("Work items export/import module configuration");
        services.AddSchemaEntry<WorkItemImportOptions>("Work item import replay lever configuration");
#endif
        services.RegisterWorkItemImportServices(configuration);
        services.AddSingleton<IWorkItemsImportCapabilityValidator, WorkItemsImportCapabilityValidator>();
        services.AddSingleton<IWorkItemsNodeReadinessOrchestrator>(sp =>
            new WorkItemsNodeReadinessOrchestrator(
                sp.GetService<NodeReadinessOrchestrator>(),
                sp.GetService<INodesOrchestrator>(),
                sp.GetService<IPlatformMetrics>(),
                sp.GetRequiredService<ILogger<WorkItemsModule>>()));
        services.AddSingleton<IWorkItemsImportOrchestrator>(sp =>
            new WorkItemsImportOrchestrator(
                sp.GetRequiredService<IWorkItemImportTargetFactory>(),
                sp.GetRequiredService<IWorkItemResolutionStrategyFactory>(),
                sp.GetRequiredService<ICheckpointingServiceFactory>(),
                sp.GetRequiredService<IIdMapStoreFactory>(),
                sp.GetRequiredService<IRevisionFolderProcessorFactory>(),
                sp.GetService<IIdentityLookupTool>(),
                sp.GetRequiredService<IWorkItemsImportCapabilityValidator>(),
                sp.GetRequiredService<IWorkItemsNodeReadinessOrchestrator>(),
                sp.GetService<IPlatformMetrics>(),
                sp.GetRequiredService<ILogger<WorkItemImportOrchestrator>>(),
                sp.GetRequiredService<ILogger<WorkItemsModule>>(),
                sp.GetRequiredService<ISourceEndpointInfo>(),
                sp.GetRequiredService<ITargetEndpointInfo>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkItemsModuleOptions>>(),
                sp.GetService<Microsoft.Extensions.Options.IOptions<WorkItemImportOptions>>(),
                sp.GetService<Microsoft.Extensions.Options.IOptions<NodesModuleOptions>>()));
        services.AddTransient<IModule, WorkItemsModule>();
        return services;
    }

    /// <summary>
    /// Registers inventory orchestration services used by module-level inventory operations.
    /// </summary>
    public static IServiceCollection AddInventoryOrchestratorServices(this IServiceCollection services)
    {
        services.AddSingleton<IInventoryOrchestrator, InventoryOrchestrator>();
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
