using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

public static class InventoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure DevOps inventory services and binds <see cref="DiscoveryOptions"/>
    /// from the <c>MigrationPlatform</c> configuration section.
    /// Also registers <see cref="IInventoryServiceFactory"/> and <see cref="InventoryDiscoveryModule"/>
    /// for agent-side use where organisations come from a <see cref="DevOpsMigrationPlatform.Abstractions.DiscoveryJob"/>.
    /// </summary>
    public static IServiceCollection AddAzureDevOpsInventory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DiscoveryOptions>().Bind(configuration.GetSection("MigrationPlatform"));
        services.AddDiscoveryOptionsOrganisationsBinder();
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IWorkItemFetchService, AzureDevOpsWorkItemFetchService>();
        services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();
        services.AddSingleton<IProjectDiscoveryService, AzureDevOpsProjectDiscoveryService>();
        services.AddSingleton<IRepoDiscoveryService, AzureDevOpsRepoDiscoveryService>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IInventoryServiceFactory, InventoryServiceFactory>();
        return services;
    }
}
