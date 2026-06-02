// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
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
    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    private static PackageContentContext ContentAt(string path)
        => new(PackageContentKind.Artefact, "test-org", "test-project", "Nodes", Address: new TestPackageAddress(path));

    private static NodesModule CreateModule(
        NodesModuleOptions? options = null,
        IClassificationTreeCapture? capture = null,
        INodesOrchestrator? orchestrator = null,
        string sourceProject = "TestProject",
        string targetProject = "TargetProject",
        IPackageAccess? package = null)
    {
        options ??= new NodesModuleOptions { Enabled = true };
        return new NodesModule(
            NullLogger<NodesModule>.Instance,
            Options.Create(options),
            sourceEndpointInfo: CreateSourceEndpointInfo(sourceProject),
            orchestrator: orchestrator ?? CreateRealOrchestrator(package),
            capture: capture,
            targetEndpointInfo: CreateTargetEndpointInfo(targetProject));
    }

    private static NodesOrchestrator CreateRealOrchestrator(IPackageAccess? package = null)
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var optsMon = new Mock<IOptionsMonitor<NodeTranslationOptions>>();
        optsMon.SetupGet(o => o.CurrentValue).Returns(opts);
        return new NodesOrchestrator(
            NullLogger<NodesOrchestrator>.Instance,
            Mock.Of<INodeTranslationTool>(),
            Mock.Of<INodeCreator>(),
            optsMon.Object,
            package: package ?? PackageTestFactory.CreateLooseMock().Object);
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

    private static ExportContext CreateExportContext(IPackageAccess package)
    {
        return new ExportContext
        {
            Job = new Job { Kind = JobKind.Export },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ImportContext CreateImportContext(IPackageAccess package)
    {
        return new ImportContext
        {
            Job = new Job { Kind = JobKind.Import },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ValidationContext CreateValidationContext(IPackageAccess package)
    {
        return new ValidationContext
        {
            Job = new Job(),
            Package = package
        };
    }

    [TestMethod]
    public async Task ExportAsync_DelegatesToCapture_WhenEnabled()
    {
        // Arrange
        var captureMock = new Mock<IClassificationTreeCapture>(MockBehavior.Strict);
        captureMock
            .Setup(c => c.CaptureAsync(
                It.IsAny<IPackageAccess>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IPlatformMetrics?>(),
                It.IsAny<string?>(),
                It.IsAny<IProgressSink?>(),
                It.IsAny<string>()))
            .Returns(Task.FromResult(0));

        var package = PackageTestFactory.CreateLooseMock().Object;
        var module = CreateModule(capture: captureMock.Object, package: package);
        var context = CreateExportContext(package);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        // Assert
        captureMock.Verify(c => c.CaptureAsync(
            It.IsAny<IPackageAccess>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
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
        var package = PackageTestFactory.CreateLooseMock().Object;
        var module = CreateModule(new NodesModuleOptions { Enabled = false }, captureMock.Object, package: package);
        var context = CreateExportContext(package);

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
        var package = PackageTestFactory.CreateLooseMock().Object;
        var module = CreateModule(opts, orchestrator: orchestratorMock.Object, package: package);
        var context = CreateImportContext(package);

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
        var package = PackageTestFactory.CreateLooseMock().Object;
        var module = CreateModule(opts, orchestrator: orchestratorMock.Object, package: package);
        var context = CreateImportContext(package);

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
        var storeMock = PackageTestFactory.CreateLooseMock();

        var module = CreateModule(package: storeMock.Object);
        var context = CreateValidationContext(storeMock.Object);

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
        var storeMock = PackageTestFactory.CreateLooseMock();
        storeMock
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Address!.RelativePath == "source-tree.json"), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("not-valid-json")))));

        var module = CreateModule(package: storeMock.Object);
        var context = CreateValidationContext(storeMock.Object);

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
        var storeMock = PackageTestFactory.CreateLooseMock();
        storeMock
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Address!.RelativePath == "source-tree.json"), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"areaNodes\":[\"ProjectA\",\"ProjectA/Sub\"],\"iterationNodes\":[]}")))));

        var module = CreateModule(package: storeMock.Object);
        var context = CreateValidationContext(storeMock.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(0, context.Errors.Count);
    }
}
