// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public class NodesModuleTests
{
    private static NodesModule CreateModule(
        NodesModuleOptions? options = null,
        IClassificationTreeCapture? capture = null,
        INodesOrchestrator? orchestrator = null,
        string sourceProject = "TestProject",
        string targetProject = "TargetProject",
        IArtefactStore? packageStore = null)
    {
        options ??= new NodesModuleOptions { Enabled = true };
        return new NodesModule(
            NullLogger<NodesModule>.Instance,
            Options.Create(options),
            sourceEndpointInfo: CreateSourceEndpointInfo(sourceProject),
            orchestrator: orchestrator ?? CreateRealOrchestrator(packageStore),
            capture: capture,
            targetEndpointInfo: CreateTargetEndpointInfo(targetProject));
    }

    private static NodesOrchestrator CreateRealOrchestrator(IArtefactStore? packageStore = null)
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var optsMon = new Mock<IOptionsMonitor<NodeTranslationOptions>>();
        optsMon.SetupGet(o => o.CurrentValue).Returns(opts);
        return new NodesOrchestrator(
            NullLogger<NodesOrchestrator>.Instance,
            Mock.Of<INodeTranslationTool>(),
            Mock.Of<INodeCreator>(),
            optsMon.Object,
            package: packageStore is null
                ? PackageTestFactory.CreateLooseMock().Object
                : PackageTestFactory.CreateDelegatingMock(packageStore).Object);
    }

    private static IAgentJobContext CreateAgentJobContext()
    {
        var mock = new Mock<IAgentJobContext>();
        mock.SetupGet(x => x.PackagePath).Returns("/tmp/test-package");
        mock.SetupGet(x => x.Mode).Returns("Export");
        mock.SetupGet(x => x.ConfigVersion).Returns("2.0");
        return mock.Object;
    }

    private static ISourceEndpointInfo CreateSourceEndpointInfo(string sourceProject = "TestProject")
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/test");
        mock.SetupGet(x => x.Project).Returns(sourceProject);
        mock.SetupGet(x => x.ConnectorType).Returns("Simulated");
        return mock.Object;
    }

    private static ITargetEndpointInfo CreateTargetEndpointInfo(string targetProject = "TargetProject")
    {
        var mock = new Mock<ITargetEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/target");
        mock.SetupGet(x => x.Project).Returns(targetProject);
        mock.SetupGet(x => x.ConnectorType).Returns("Simulated");
        return mock.Object;
    }

    private static ExportContext CreateExportContext(IArtefactStore store)
    {
        return new ExportContext
        {
            Job = new Job { Kind = JobKind.Export },
            ArtefactStore = store,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ImportContext CreateImportContext(IArtefactStore store)
    {
        return new ImportContext
        {
            Job = new Job { Kind = JobKind.Import },
            ArtefactStore = store,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ValidationContext CreateValidationContext(Mock<IArtefactStore> store)
    {
        return new ValidationContext
        {
            Job = new Job(),
            ArtefactStore = store.Object
        };
    }

    [TestMethod]
    public async Task ExportAsync_DelegatesToCapture_WhenEnabled()
    {
        // Arrange
        var captureMock = new Mock<IClassificationTreeCapture>(MockBehavior.Strict);
        captureMock
            .Setup(c => c.CaptureAsync(
                It.IsAny<IArtefactStore>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IPlatformMetrics?>(),
                It.IsAny<string?>(),
                It.IsAny<IProgressSink?>(),
                It.IsAny<string>()))
            .Returns(Task.FromResult(0));

        var store = Mock.Of<IArtefactStore>();
        var module = CreateModule(capture: captureMock.Object, packageStore: store);
        var context = CreateExportContext(store);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        // Assert
        captureMock.Verify(c => c.CaptureAsync(
            It.IsAny<IArtefactStore>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IPlatformMetrics?>(),
            It.IsAny<string?>(),
            It.IsAny<IProgressSink?>(),
            It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public async Task ExportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var captureMock = new Mock<IClassificationTreeCapture>(MockBehavior.Strict);
        var store = Mock.Of<IArtefactStore>();
        var module = CreateModule(new NodesModuleOptions { Enabled = false }, captureMock.Object, packageStore: store);
        var context = CreateExportContext(store);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        captureMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ImportAsync_CallsReplicateSourceTree_WhenOptionEnabled()
    {
        // Arrange
        var orchestratorMock = new Mock<INodesOrchestrator>(MockBehavior.Loose);
        orchestratorMock
            .Setup(o => o.ImportAsync(
                It.IsAny<ImportContext>(),
                It.IsAny<ISourceEndpointInfo>(),
                It.IsAny<ITargetEndpointInfo>(),
                It.IsAny<ICheckpointingServiceFactory?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opts = new NodesModuleOptions { Enabled = true, ReplicateSourceTree = true };
        var store = Mock.Of<IArtefactStore>();
        var module = CreateModule(opts, orchestrator: orchestratorMock.Object, packageStore: store);
        var context = CreateImportContext(store);

        // Act
        await module.ImportAsync(context, CancellationToken.None);

        // Assert
        orchestratorMock.Verify(o => o.ImportAsync(
            It.IsAny<ImportContext>(),
            It.IsAny<ISourceEndpointInfo>(),
            It.IsAny<ITargetEndpointInfo>(),
            It.IsAny<ICheckpointingServiceFactory?>(),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ImportAsync_DoesNotCallEnsurer_WhenReplicateSourceTreeDisabled()
    {
        // Arrange
        var orchestratorMock = new Mock<INodesOrchestrator>(MockBehavior.Strict);
        orchestratorMock
            .Setup(o => o.ImportAsync(
                It.IsAny<ImportContext>(),
                It.IsAny<ISourceEndpointInfo>(),
                It.IsAny<ITargetEndpointInfo>(),
                It.IsAny<ICheckpointingServiceFactory?>(),
                false,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opts = new NodesModuleOptions
        {
            Enabled = true,
            ReplicateSourceTree = false
        };
        var store = Mock.Of<IArtefactStore>();
        var module = CreateModule(opts, orchestrator: orchestratorMock.Object, packageStore: store);
        var context = CreateImportContext(store);

        // Act
        await module.ImportAsync(context, CancellationToken.None);

        // Assert — orchestrator is called with replicateSourceTree=false
        orchestratorMock.Verify(o => o.ImportAsync(
            It.IsAny<ImportContext>(),
            It.IsAny<ISourceEndpointInfo>(),
            It.IsAny<ITargetEndpointInfo>(),
            It.IsAny<ICheckpointingServiceFactory?>(),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenSourceTreeJsonMissing()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var module = CreateModule(packageStore: storeMock.Object);
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "source-tree.json");
    }

    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenSourceTreeJsonIsMalformed()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock
            .Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("not-valid-json");

        var module = CreateModule(packageStore: storeMock.Object);
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "malformed");
    }

    [TestMethod]
    public async Task ValidateAsync_PassesForValidSourceTreeJson()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock
            .Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"areaNodes\":[\"ProjectA\",\"ProjectA/Sub\"],\"iterationNodes\":[]}");

        var module = CreateModule(packageStore: storeMock.Object);
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(0, context.Errors.Count);
    }
}
