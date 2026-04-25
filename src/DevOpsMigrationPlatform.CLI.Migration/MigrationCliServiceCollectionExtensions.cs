using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.CLI.Migration;

/// <summary>
/// Composition-root extensions for the migration CLI.
/// This is the ONLY file in <c>DevOpsMigrationPlatform.CLI.Migration</c> that is
/// permitted to reference <c>DevOpsMigrationPlatform.Infrastructure.AzureDevOps</c>.
/// Individual command classes must call these methods rather than calling
/// infrastructure extension methods directly.
/// </summary>
public static class MigrationCliServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ADO endpoint option types for polymorphic JSON deserialization.
    /// Called by the shared host builder so every CLI command can load configuration files
    /// that reference <c>"Type": "AzureDevOpsServices"</c>.
    /// </summary>
    public static IServiceCollection AddMigrationCliEndpointTypes(
        this IServiceCollection services)
    {
        services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        services.AddOrganisationEntryType("AzureDevOpsServices", typeof(AzureDevOpsOrganisationEntry));
        services.AddSimulatedServices();
        return services;
    }
}
