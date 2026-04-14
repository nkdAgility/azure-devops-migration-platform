using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Extension methods to register dependency analysis services with the DI container.
/// Configures keyed registrations for multiple IWorkItemLinkAnalysisService implementations.
/// </summary>
public static class DependencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure DevOps dependency analysis services.
    /// Binds DiscoveryOptions from configuration, registers IDependencyDiscoveryService,
    /// and registers keyed singletons for different source types.
    /// </summary>
    public static IServiceCollection AddAzureDevOpsDependencyAnalysis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Bind DiscoveryOptions from configuration root
        services.Configure<DiscoveryOptions>(configuration);

        // Register the Azure DevOps client factory (if not already registered)
        if (!services.Any(x => x.ServiceType == typeof(IAzureDevOpsClientFactory)))
        {
            services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        }

        // Register WIQL query dependencies needed by work item discovery
        if (!services.Any(x => x.ServiceType == typeof(IWiqlQueryClientFactory)))
        {
            services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        }

        if (!services.Any(x => x.ServiceType == typeof(IWorkItemQueryWindowStrategy)))
        {
            services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        }

        // Register discovery services needed by CatalogService and other components
        if (!services.Any(x => x.ServiceType == typeof(IWorkItemDiscoveryService)))
        {
            services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();
        }

        if (!services.Any(x => x.ServiceType == typeof(IProjectDiscoveryService)))
        {
            services.AddSingleton<IProjectDiscoveryService, AzureDevOpsProjectDiscoveryService>();
        }

        // Register the catalog service (for querying available projects)
        if (!services.Any(x => x.ServiceType == typeof(ICatalogService)))
        {
            services.AddSingleton<ICatalogService, CatalogService>();
        }

        // Register AzureDevOpsDependencyAnalysisService as a keyed singleton
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService, AzureDevOpsDependencyAnalysisService>(
            serviceKey: "AzureDevOpsServices");

        // Register the orchestrator service
        services.AddSingleton<IDependencyDiscoveryService, DependencyDiscoveryService>();

        return services;
    }
}
