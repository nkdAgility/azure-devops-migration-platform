using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

public static class InventoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure DevOps inventory services and binds <see cref="DiscoveryOptions"/>
    /// from the <c>MigrationTools</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAzureDevOpsInventory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DiscoveryOptions>().Bind(configuration);
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();
        services.AddSingleton<IProjectDiscoveryService, AzureDevOpsProjectDiscoveryService>();
        services.AddSingleton<IRepoDiscoveryService, AzureDevOpsRepoDiscoveryService>();
        services.AddSingleton<IInventoryService, InventoryService>();
        return services;
    }
}
