using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

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

        // Bind DiscoveryOptions from the MigrationPlatform configuration section
        services.Configure<DiscoveryOptions>(configuration.GetSection("MigrationPlatform"));
        services.AddDiscoveryOptionsOrganisationsBinder();

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
        if (!services.Any(x => x.ServiceType == typeof(IWorkItemFetchService)))
        {
            services.AddSingleton<IWorkItemFetchService, AzureDevOpsWorkItemFetchService>();
        }

        if (!services.Any(x => x.ServiceType == typeof(IWorkItemDiscoveryService)))
        {
            services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();
        }

        if (!services.Any(x => x.ServiceType == typeof(IProjectDiscoveryService)))
        {
            services.AddSingleton<IProjectDiscoveryService, AzureDevOpsProjectDiscoveryService>();
        }

        // ICatalogService must be registered by the host before calling this method.
        // The standard registration is AddSingleton<ICatalogService, CatalogService>() in
        // MigrationAgentServiceExtensions.cs. Failing fast here makes the ordering
        // requirement explicit and prevents silent misconfiguration.
        if (!services.Any(x => x.ServiceType == typeof(ICatalogService)))
            throw new InvalidOperationException(
                "ICatalogService is not registered. Call 'services.AddSingleton<ICatalogService, CatalogService>()' " +
                "(or equivalent) before calling AddAzureDevOpsDependencyAnalysis().");

        // Register AzureDevOpsDependencyAnalysisService as a keyed singleton
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService, AzureDevOpsDependencyAnalysisService>(
            serviceKey: "AzureDevOpsServices");

        // Register the orchestrator service
        services.AddSingleton<IDependencyDiscoveryService, DependencyDiscoveryService>();

        // Factory for agent-side use where organisations come from DiscoveryJob (not config).
        services.AddSingleton<IDependencyDiscoveryServiceFactory, DependencyDiscoveryServiceFactory>();

        return services;
    }
}
