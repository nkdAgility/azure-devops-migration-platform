// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Nodes;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemType;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;

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
    ///   <item><see cref="IWorkItemTargetFactory"/> as <see cref="AzureDevOpsWorkItemTargetFactory"/>
    ///   keyed to <c>"AzureDevOpsServices"</c>.</item>
    ///   <item><see cref="IWorkItemResolutionStrategyFactory"/> as <see cref="AzureDevOpsResolutionStrategyFactory"/>.</item>
    ///   <item><see cref="IIdentityMappingService"/> as <see cref="PassThroughIdentityMappingService"/>.</item>
    /// </list>
    /// Requires <see cref="IAzureDevOpsClientFactory"/> to already be registered
    /// (provided by <c>AddAzureDevOpsWorkItemExport</c>).
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItem(this IServiceCollection services)
    {
        // Register ADO import target factory as a keyed entry in the composite dispatcher
        services.AddImportTargetFactory<AzureDevOpsWorkItemTargetFactory>("AzureDevOpsServices");
        services.AddWorkItemTypeReadinessTargetFactory<AzureDevOpsWorkItemTypeReadinessTargetFactory>("AzureDevOpsServices");
        // Register ADO resolution strategy factory as a keyed entry in the composite dispatcher
        services.AddResolutionStrategyFactory<AzureDevOpsResolutionStrategyFactory, AzureDevOpsWorkItemTarget>();
        services.TryAddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
        services.AddIdentityLookupToolServices();
        services.AddSingleton<ICheckpointingServiceFactory, CheckpointingServiceFactory>();
        services.AddSingleton<IIdMapStoreFactory, IdMapStoreFactory>();
        services.AddScoped<IWorkItemResolutionProcessorFactory, RevisionFolderProcessorFactory>();
        // Classification node creator — creates area/iteration nodes in the target ADO project.
        services.AddNodeCreator<AzureDevOpsNodeCreator>("AzureDevOpsServices");
        services.AddProjectLifecycleProvider<AzureDevOpsProjectLifecycleProvider>("AzureDevOpsServices");
        services.AddProjectProcessProvider<AzureDevOpsProjectProcessProvider>("AzureDevOpsServices");
        return services;
    }
}
