using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;
using DevOpsMigrationPlatform.Infrastructure.Import;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Registers Azure DevOps work item import services for the IoC container.
/// Call this alongside <see cref="ExportServiceCollectionExtensions.AddAzureDevOpsWorkItemExport"/>
/// when import functionality is required.
/// </summary>
public static class ImportServiceCollectionExtensions
{
    /// <summary>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="IWorkItemImportTargetFactory"/> as <see cref="AzureDevOpsWorkItemImportTargetFactory"/>.</item>
    ///   <item><see cref="IWorkItemResolutionStrategy"/> as <see cref="NullResolutionStrategy"/> (default — no live seeding).</item>
    ///   <item><see cref="IIdentityMappingService"/> as <see cref="PassThroughIdentityMappingService"/> (default — pass-through, extended in US4).</item>
    /// </list>
    /// Requires <see cref="IAzureDevOpsClientFactory"/> to already be registered
    /// (provided by <c>AddAzureDevOpsWorkItemExport</c>).
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemImport(this IServiceCollection services)
    {
        services.AddSingleton<IWorkItemImportTargetFactory, AzureDevOpsWorkItemImportTargetFactory>();
        services.AddSingleton<IWorkItemResolutionStrategy, NullResolutionStrategy>();
        services.AddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
        return services;
    }

    /// <summary>
    /// Registers a simulated (in-memory) import target factory for offline and integration testing.
    /// Target type "Simulated" in the scenario config will use this factory.
    /// </summary>
    public static IServiceCollection AddSimulatedWorkItemImport(this IServiceCollection services)
    {
        services.AddSingleton<IWorkItemImportTargetFactory, SimulatedWorkItemImportTargetFactory>();
        services.AddSingleton<IWorkItemResolutionStrategy, NullResolutionStrategy>();
        services.AddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
        return services;
    }
}
