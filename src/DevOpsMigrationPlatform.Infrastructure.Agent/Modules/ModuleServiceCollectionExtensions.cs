// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;
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
        services.AddInventoryOrchestratorServices();
        services.AddInventoryAnalyserServices();
        services.AddDependencyAnalyserServices();
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
        services.AddTransient<IImportFailurePattern, MissingRevisionArtefactImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, InvalidRevisionPayloadImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, MissingAttachmentBinaryImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, MissingEmbeddedImageBinaryImportFailurePattern>();
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
