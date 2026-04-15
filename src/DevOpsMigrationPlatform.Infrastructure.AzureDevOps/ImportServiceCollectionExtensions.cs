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
    ///   <item><see cref="IWorkItemResolutionStrategyFactory"/> as <see cref="AzureDevOpsResolutionStrategyFactory"/> (selects strategy at runtime from module config).</item>
    ///   <item><see cref="IIdentityMappingService"/> as <see cref="PassThroughIdentityMappingService"/> (default — pass-through, extended in US4).</item>
    /// </list>
    /// Requires <see cref="IAzureDevOpsClientFactory"/> to already be registered
    /// (provided by <c>AddAzureDevOpsWorkItemExport</c>).
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemImport(this IServiceCollection services)
    {
        services.AddSingleton<IWorkItemImportTargetFactory, AzureDevOpsWorkItemImportTargetFactory>();
        services.AddSingleton<IWorkItemResolutionStrategyFactory, AzureDevOpsResolutionStrategyFactory>();
        services.AddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
        return services;
    }

    /// <summary>
    /// Registers a simulated (in-memory) import target factory for offline and integration testing.
    /// Target type "Simulated" in the scenario config will use this factory.
    /// <para>
    /// Note: <see cref="AzureDevOpsResolutionStrategyFactory"/> is also registered so that
    /// import jobs that require a resolution strategy can still be configured; however, executing
    /// a resolution strategy against a <see cref="SimulatedWorkItemImportTarget"/> will throw
    /// because the simulated target does not expose an organisation URL.
    /// For tests that mock the strategy directly this is not an issue.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSimulatedWorkItemImport(this IServiceCollection services)
    {
        services.AddSingleton<IWorkItemImportTargetFactory, SimulatedWorkItemImportTargetFactory>();
        services.AddSingleton<IWorkItemResolutionStrategyFactory, AzureDevOpsResolutionStrategyFactory>();
        services.AddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
        return services;
    }
}
