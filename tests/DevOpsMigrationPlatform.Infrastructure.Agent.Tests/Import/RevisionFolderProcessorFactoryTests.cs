// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class RevisionFolderProcessorFactoryTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WithBaseOverload_ReturnsWorkItemResolutionProcessor()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        var sut = new RevisionFolderProcessorFactory(
            NullLoggerFactory.Instance,
            package.Object,
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) });

        var processor = sut.Create(
            target: Mock.Of<IWorkItemTarget>(),
            idMapStore: Mock.Of<IIdMapStore>(),
            checkpointing: Mock.Of<ICheckpointingService>(),
            identityTranslationTool: null,
            organisation: "https://dev.azure.com/org",
            project: "Project");

        Assert.IsInstanceOfType<WorkItemResolutionProcessor>(processor);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) },
            metrics: null,
            fieldTransformTool: null,
            nodeStructureTool: Mock.Of<INodeTranslationTool>(),
            nodeStructureOptions: nodeOptions);

        var processor = sut.Create(
            target: Mock.Of<IWorkItemTarget>(),
            idMapStore: Mock.Of<IIdMapStore>(),
            checkpointing: Mock.Of<ICheckpointingService>(),
            identityTranslationTool: Mock.Of<IIdentityTranslationTool>(),
            organisation: "https://dev.azure.com/org",
            project: "Project",
            nodeStructureContext: new ProjectMapping("Source", "Target"));

        Assert.IsInstanceOfType<WorkItemResolutionProcessor>(processor);
    }
}
