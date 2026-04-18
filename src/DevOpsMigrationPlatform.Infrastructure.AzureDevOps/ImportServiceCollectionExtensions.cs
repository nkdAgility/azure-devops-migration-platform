using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
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
    ///   <item><see cref="IWorkItemImportTargetFactory"/> as <see cref="AzureDevOpsWorkItemImportTargetFactory"/>
    ///   keyed to <c>"AzureDevOpsServices"</c>.</item>
    ///   <item><see cref="IWorkItemResolutionStrategyFactory"/> as <see cref="AzureDevOpsResolutionStrategyFactory"/>.</item>
    ///   <item><see cref="IIdentityMappingService"/> as <see cref="PassThroughIdentityMappingService"/>.</item>
    /// </list>
    /// Requires <see cref="IAzureDevOpsClientFactory"/> to already be registered
    /// (provided by <c>AddAzureDevOpsWorkItemExport</c>).
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemImport(this IServiceCollection services)
    {
        // Register ADO import target factory as a keyed entry in the composite dispatcher
        services.AddImportTargetFactory<AzureDevOpsWorkItemImportTargetFactory>("AzureDevOpsServices");
        // Register ADO resolution strategy factory as a keyed entry in the composite dispatcher
        services.AddResolutionStrategyFactory<AzureDevOpsResolutionStrategyFactory, AzureDevOpsWorkItemImportTarget>();
        services.AddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
        services.AddSingleton<ICheckpointingServiceFactory, CheckpointingServiceFactory>();
        services.AddSingleton<IIdMapStoreFactory, IdMapStoreFactory>();
        services.AddSingleton<IRevisionFolderProcessorFactory, RevisionFolderProcessorFactory>();
        return services;
    }
}
