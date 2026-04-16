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
    ///   <item><see cref="IWorkItemImportTargetFactory"/> as <see cref="AzureDevOpsWorkItemImportTargetFactory"/>.
    ///   Routes to <c>SimulatedWorkItemImportTarget</c> or <c>AzureDevOpsWorkItemImportTarget</c>
    ///   at runtime based on <c>target.type</c> in the scenario config.</item>
    ///   <item><see cref="IWorkItemResolutionStrategyFactory"/> as <see cref="AzureDevOpsResolutionStrategyFactory"/>.</item>
    ///   <item><see cref="IIdentityMappingService"/> as <see cref="PassThroughIdentityMappingService"/>.</item>
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
}
