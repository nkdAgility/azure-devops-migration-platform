// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;

public sealed class WorkItemsOrchestratorFactory : IWorkItemsOrchestratorFactory
{
    public IWorkItemsOrchestrator Create(
        IWorkItemTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IWorkItemResolutionProcessorFactory processorFactory,
        IIdentityLookupTool? identityLookupTool,
        IWorkItemsImportCapabilityValidator capabilityValidator,
        IWorkItemsNodeReadinessOrchestrator nodeReadinessOrchestrator,
        IPlatformMetrics? metrics,
        ILogger<WorkItemOrchestrator> orchestratorLogger,
        ILogger<WorkItemsModule> moduleLogger,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        IOptions<WorkItemsModuleOptions> options,
        IOptions<WorkItemOptions>? workItemImportOptions,
        IOptions<NodesModuleOptions>? nodesModuleOptions)
        => new WorkItemOrchestrator(
            importTargetFactory,
            resolutionStrategyFactory,
            checkpointingFactory,
            idMapStoreFactory,
            processorFactory,
            identityLookupTool,
            capabilityValidator,
            nodeReadinessOrchestrator,
            metrics,
            orchestratorLogger,
            moduleLogger,
            sourceEndpointInfo,
            targetEndpointInfo,
            options,
            workItemImportOptions,
            nodesModuleOptions);
}
