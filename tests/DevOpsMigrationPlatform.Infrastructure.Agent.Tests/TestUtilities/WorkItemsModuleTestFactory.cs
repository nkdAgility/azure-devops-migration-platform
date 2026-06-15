// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Nodes;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

/// <summary>
/// Builds a <see cref="WorkItemsModule"/> (thin façade) over a fully-constructed
/// <see cref="WorkItemsOrchestrator"/>. The module ctor no longer composes the graph (ADR 0019 —
/// composition moved to DI); this helper performs that composition for tests, with sensible mock
/// defaults. Pass only the dependencies a given test needs to control.
/// </summary>
internal static class WorkItemsModuleTestFactory
{
    public static WorkItemsModule Create(
        IWorkItemRevisionSourceFactory? sourceFactory = null,
        ILogger<WorkItemsModule>? logger = null,
        IOptions<WorkItemsModuleOptions>? options = null,
        ISourceEndpointInfo? sourceEndpointInfo = null,
        IWorkItemTargetFactory? importTargetFactory = null,
        IWorkItemResolutionStrategyFactory? resolutionStrategyFactory = null,
        ICheckpointingServiceFactory? checkpointingFactory = null,
        IIdMapStoreFactory? idMapStoreFactory = null,
        IWorkItemResolutionProcessorFactory? processorFactory = null,
        ITargetEndpointInfo? targetEndpointInfo = null,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        IWorkItemFetchService? fetchService = null,
        IInventoryOrchestrator? inventoryOrchestrator = null,
        IPlatformMetrics? metrics = null,
        IWorkItemDiscoveryService? discoveryService = null,
        IExportProgressStoreFactory? exportProgressStoreFactory = null,
        IReferencedPathTracker? referencedPathTracker = null,
        INodesOrchestrator? nodesOrchestrator = null,
        NodeReadinessOrchestrator? nodeReadinessOrchestrator = null,
        IOptions<NodesModuleOptions>? nodesModuleOptions = null,
        IFieldTransformTool? fieldTransformTool = null,
        IOptions<WorkItemOptions>? workItemImportOptions = null,
        IWorkItemExportOrchestratorFactory? exportOrchestratorFactory = null,
        IIdentityTranslationTool? identityTranslationTool = null,
        IRepoDiscoveryService? repoDiscoveryService = null,
        IEnumerable<IImportFailurePattern>? importFailurePatterns = null,
        ImportPreparer? importPreparer = null)
        => new(CreateOrchestrator(
            sourceFactory, logger, options, sourceEndpointInfo, importTargetFactory,
            resolutionStrategyFactory, checkpointingFactory, idMapStoreFactory, processorFactory,
            targetEndpointInfo, attachmentBinarySource, inlineCommentSourceFactory, fetchService,
            inventoryOrchestrator, metrics, discoveryService, exportProgressStoreFactory,
            referencedPathTracker, nodesOrchestrator, nodeReadinessOrchestrator, nodesModuleOptions,
            fieldTransformTool, workItemImportOptions, exportOrchestratorFactory, identityTranslationTool,
            repoDiscoveryService, importFailurePatterns, importPreparer));

    public static WorkItemsOrchestrator CreateOrchestrator(
        IWorkItemRevisionSourceFactory? sourceFactory = null,
        ILogger<WorkItemsModule>? logger = null,
        IOptions<WorkItemsModuleOptions>? options = null,
        ISourceEndpointInfo? sourceEndpointInfo = null,
        IWorkItemTargetFactory? importTargetFactory = null,
        IWorkItemResolutionStrategyFactory? resolutionStrategyFactory = null,
        ICheckpointingServiceFactory? checkpointingFactory = null,
        IIdMapStoreFactory? idMapStoreFactory = null,
        IWorkItemResolutionProcessorFactory? processorFactory = null,
        ITargetEndpointInfo? targetEndpointInfo = null,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        IWorkItemFetchService? fetchService = null,
        IInventoryOrchestrator? inventoryOrchestrator = null,
        IPlatformMetrics? metrics = null,
        IWorkItemDiscoveryService? discoveryService = null,
        IExportProgressStoreFactory? exportProgressStoreFactory = null,
        IReferencedPathTracker? referencedPathTracker = null,
        INodesOrchestrator? nodesOrchestrator = null,
        NodeReadinessOrchestrator? nodeReadinessOrchestrator = null,
        IOptions<NodesModuleOptions>? nodesModuleOptions = null,
        IFieldTransformTool? fieldTransformTool = null,
        IOptions<WorkItemOptions>? workItemImportOptions = null,
        IWorkItemExportOrchestratorFactory? exportOrchestratorFactory = null,
        IIdentityTranslationTool? identityTranslationTool = null,
        IRepoDiscoveryService? repoDiscoveryService = null,
        IEnumerable<IImportFailurePattern>? importFailurePatterns = null,
        ImportPreparer? importPreparer = null)
    {
        options ??= Options.Create(new WorkItemsModuleOptions());
        logger ??= NullLogger<WorkItemsModule>.Instance;
        sourceEndpointInfo ??= DefaultSourceEndpoint();
        targetEndpointInfo ??= DefaultTargetEndpoint();
        var cpf = checkpointingFactory ?? Mock.Of<ICheckpointingServiceFactory>();
        var fieldTransform = fieldTransformTool ?? Mock.Of<IFieldTransformTool>();

        var patterns = importFailurePatterns?.ToArray();
        patterns = patterns is { Length: > 0 }
            ? patterns
            : new IImportFailurePattern[]
            {
                new MissingRevisionArtefactImportFailurePattern(),
                new InvalidRevisionPayloadImportFailurePattern(),
                new MissingAttachmentBinaryImportFailurePattern(),
                new MissingEmbeddedImageBinaryImportFailurePattern(),
                new FieldTransformCompatibilityImportFailurePattern()
            };
        var preparer = importPreparer ?? new ImportPreparer(
            options, sourceEndpointInfo.OrganisationSlug, sourceEndpointInfo.Project, patterns);

        return new WorkItemsOrchestrator(
            sourceFactory ?? Mock.Of<IWorkItemRevisionSourceFactory>(),
            attachmentBinarySource,
            inlineCommentSourceFactory,
            fetchService,
            exportOrchestratorFactory ?? new WorkItemExportOrchestratorFactory(),
            cpf,
            logger,
            metrics,
            discoveryService,
            exportProgressStoreFactory,
            referencedPathTracker,
            options,
            sourceEndpointInfo,
            preparer,
            importTargetFactory ?? Mock.Of<IWorkItemTargetFactory>(),
            resolutionStrategyFactory ?? Mock.Of<IWorkItemResolutionStrategyFactory>(),
            idMapStoreFactory ?? Mock.Of<IIdMapStoreFactory>(),
            processorFactory ?? Mock.Of<IWorkItemResolutionProcessorFactory>(),
            identityTranslationTool,
            new WorkItemsImportCapabilityValidator(fieldTransform),
            new WorkItemsNodeReadinessOrchestrator(nodeReadinessOrchestrator, nodesOrchestrator, metrics, logger),
            targetEndpointInfo,
            workItemImportOptions,
            nodesModuleOptions,
            inventoryOrchestrator,
            repoDiscoveryService);
    }

    private static ISourceEndpointInfo DefaultSourceEndpoint()
    {
        var m = new Mock<ISourceEndpointInfo>();
        m.SetupGet(s => s.Project).Returns("ProjectA");
        m.SetupGet(s => s.Url).Returns("https://source.example");
        m.SetupGet(s => s.ConnectorType).Returns("Simulated");
        m.SetupGet(s => s.OrganisationSlug).Returns("test-org");
        return m.Object;
    }

    private static ITargetEndpointInfo DefaultTargetEndpoint()
    {
        var m = new Mock<ITargetEndpointInfo>();
        m.SetupGet(s => s.Project).Returns("ProjectA");
        m.SetupGet(s => s.Url).Returns("https://target.example");
        m.SetupGet(s => s.ConnectorType).Returns("Simulated");
        m.SetupGet(s => s.OrganisationSlug).Returns("test-target-org");
        return m.Object;
    }
}
