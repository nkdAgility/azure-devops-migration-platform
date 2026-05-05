// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

public static class InventoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure DevOps inventory services and binds <see cref="MigrationOptions"/>
    /// from the <c>MigrationPlatform</c> configuration section.
    /// Also registers <see cref="IInventoryServiceFactory"/> and <see cref="InventoryDiscoveryModule"/>
    /// for agent-side use where organisations come from a <see cref="DevOpsMigrationPlatform.Abstractions.DiscoveryJob"/>.
    /// </summary>
    public static IServiceCollection AddAzureDevOpsInventory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MigrationOptions>().Bind(configuration.GetSection("MigrationPlatform"));
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IWorkItemFetchService, AzureDevOpsWorkItemFetchService>();
        services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();
        services.AddSingleton<IProjectDiscoveryService, AzureDevOpsProjectDiscoveryService>();
        services.AddSingleton<IRepoDiscoveryService, AzureDevOpsRepoDiscoveryService>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IInventoryServiceFactory, InventoryServiceFactory>();
        // Keyed registration — resolves when connector type is "AzureDevOpsServices" or "TeamFoundationServer".
        services.AddKeyedSingleton<IInventoryServiceFactory, InventoryServiceFactory>("AzureDevOpsServices");
        services.AddKeyedSingleton<IInventoryServiceFactory, InventoryServiceFactory>("TeamFoundationServer");
        return services;
    }
}
