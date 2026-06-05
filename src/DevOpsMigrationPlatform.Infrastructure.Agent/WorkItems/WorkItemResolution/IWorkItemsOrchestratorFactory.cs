// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

/// <summary>
/// Creates WorkItems module orchestrators without inlining concrete construction inside the module wrapper.
/// </summary>
public interface IWorkItemsOrchestratorFactory
{
    IWorkItemsOrchestrator Create(
        IWorkItemTargetFactory importTargetFactory,
        IWorkItemResolutionStrategyFactory resolutionStrategyFactory,
        ICheckpointingServiceFactory checkpointingFactory,
        IIdMapStoreFactory idMapStoreFactory,
        IWorkItemResolutionProcessorFactory processorFactory,
        IIdentityTranslationTool? identityTranslationTool,
        IWorkItemsImportCapabilityValidator capabilityValidator,
        IWorkItemsNodeReadinessOrchestrator nodeReadinessOrchestrator,
        IPlatformMetrics? metrics,
        ILogger<WorkItemsImportRuntime> orchestratorLogger,
        ILogger<WorkItemsModule> moduleLogger,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        IOptions<WorkItemsModuleOptions> options,
        IOptions<WorkItemOptions>? workItemImportOptions,
        IOptions<NodesModuleOptions>? nodesModuleOptions,
        IWorkItemRevisionSourceFactory sourceFactory,
        IAttachmentBinarySource? attachmentBinarySource,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory,
        IWorkItemFetchService? fetchService,
        IWorkItemExportOrchestratorFactory exportOrchestratorFactory,
        IWorkItemDiscoveryService? discoveryService,
        IExportProgressStoreFactory? exportProgressStoreFactory,
        IReferencedPathTracker? referencedPathTracker,
        ImportPreparer importPreparer);
}
