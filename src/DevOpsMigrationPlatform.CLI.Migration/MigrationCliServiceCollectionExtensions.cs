using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.CLI.Migration;

/// <summary>
/// Composition-root extensions for the migration CLI.
/// Registers all endpoint option types required for polymorphic JSON deserialization.
/// Individual command classes must call these methods rather than calling
/// infrastructure extension methods directly.
/// </summary>
public static class MigrationCliServiceCollectionExtensions
{
    /// <summary>
    /// Registers the endpoint option types for polymorphic JSON deserialization.
    /// Called by the shared host builder so every CLI command can load configuration files
    /// that reference any supported <c>"Type"</c> value.
    /// </summary>
    public static IServiceCollection AddMigrationCliEndpointTypes(
        this IServiceCollection services)
    {
        services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        services.AddOrganisationEntryType("AzureDevOpsServices", typeof(AzureDevOpsOrganisationEntry));
        services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions));
        services.AddOrganisationEntryType("Simulated", typeof(SimulatedOrganisationEntry));
        return services;
    }
}
