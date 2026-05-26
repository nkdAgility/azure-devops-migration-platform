// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class RevisionFolderProcessorFactoryTests
{
    [TestMethod]
    public void Create_WithBaseOverload_ReturnsWorkItemResolutionProcessor()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        var sut = new RevisionFolderProcessorFactory(
            NullLoggerFactory.Instance,
            package.Object);

        var processor = sut.Create(
            target: Mock.Of<IWorkItemImportTarget>(),
            idMapStore: Mock.Of<IIdMapStore>(),
            checkpointing: Mock.Of<ICheckpointingService>(),
            identityLookupTool: null,
            organisation: "https://dev.azure.com/org",
            project: "Project");

        Assert.IsInstanceOfType<WorkItemResolutionProcessor>(processor);
    }

    [TestMethod]
    public void Create_WithNodeTranslationContext_ReturnsWorkItemResolutionProcessor()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        var nodeOptions = Options.Create(new NodeTranslationOptions
        {
            SkipOnUnresolvableArea = true,
            SkipOnUnresolvableIteration = true
        });

        var sut = new RevisionFolderProcessorFactory(
            NullLoggerFactory.Instance,
            package.Object,
            metrics: null,
            fieldTransformTool: null,
            nodeStructureTool: Mock.Of<INodeTranslationTool>(),
            nodeStructureOptions: nodeOptions);

        var processor = sut.Create(
            target: Mock.Of<IWorkItemImportTarget>(),
            idMapStore: Mock.Of<IIdMapStore>(),
            checkpointing: Mock.Of<ICheckpointingService>(),
            identityLookupTool: Mock.Of<IIdentityLookupTool>(),
            organisation: "https://dev.azure.com/org",
            project: "Project",
            nodeStructureContext: new ProjectMapping("Source", "Target"));

        Assert.IsInstanceOfType<WorkItemResolutionProcessor>(processor);
    }
}
