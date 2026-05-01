using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
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
        INodeEnsurer? nodeEnsurer = null,
        ActiveJobConfigState? activeJobConfig = null)
    {
        options ??= new NodesModuleOptions { Enabled = true };
        activeJobConfig ??= CreateActiveJobConfig();
        return new NodesModule(
            NullLogger<NodesModule>.Instance,
            Options.Create(options),
            agentJobContext: CreateAgentJobContext(activeJobConfig),
            sourceEndpointInfo: CreateSourceEndpointInfo(activeJobConfig),
            capture: capture,
            targetEndpointInfo: Mock.Of<ITargetEndpointInfo>(),
            nodeEnsurer: nodeEnsurer);
    }

    private static ActiveJobConfigState CreateActiveJobConfig(
        string sourceProject = "TestProject",
        string targetProject = "TargetProject")
    {
        var state = new ActiveJobConfigState();
        state.Current = new MigrationOptions
        {
            Source = new SimulatedEndpointOptions { Project = sourceProject },
            Target = new SimulatedEndpointOptions { Project = targetProject }
        };
        return state;
    }

    private static IAgentJobContext CreateAgentJobContext(ActiveJobConfigState activeJobConfig)
    {
        var mock = new Mock<IAgentJobContext>();
        mock.SetupGet(x => x.PackagePath).Returns("/tmp/test-package");
        mock.SetupGet(x => x.Mode).Returns("Export");
        mock.SetupGet(x => x.ConfigVersion).Returns("2.0");
        return mock.Object;
    }

    private static ISourceEndpointInfo CreateSourceEndpointInfo(ActiveJobConfigState activeJobConfig)
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/test");
        mock.SetupGet(x => x.Project).Returns(activeJobConfig.Current?.Source?.GetProject() ?? "TestProject");
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
                It.IsAny<IMigrationMetrics?>(),
                It.IsAny<string?>(),
                It.IsAny<IProgressSink?>(),
                It.IsAny<string>()))
            .Returns(Task.FromResult(0));

        var module = CreateModule(capture: captureMock.Object, activeJobConfig: CreateActiveJobConfig());
        var store = Mock.Of<IArtefactStore>();
        var context = CreateExportContext(store);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        // Assert
        captureMock.Verify(c => c.CaptureAsync(
            It.IsAny<IArtefactStore>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IMigrationMetrics?>(),
            It.IsAny<string?>(),
            It.IsAny<IProgressSink?>(),
            It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public async Task ExportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var captureMock = new Mock<IClassificationTreeCapture>(MockBehavior.Strict);
        var module = CreateModule(new NodesModuleOptions { Enabled = false }, captureMock.Object);
        var store = Mock.Of<IArtefactStore>();
        var context = CreateExportContext(store);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        captureMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ImportAsync_CallsReplicateSourceTree_WhenOptionEnabled()
    {
        // Arrange
        var ensurerMock = new Mock<INodeEnsurer>(MockBehavior.Loose);
        ensurerMock
            .Setup(e => e.ReplicateSourceTreeAsync(
                It.IsAny<ProjectMapping>(),
                It.IsAny<IArtefactStore>(),
                It.IsAny<IStateStore>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IMigrationMetrics?>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var opts = new NodesModuleOptions { Enabled = true, ReplicateSourceTree = true };
        var module = CreateModule(opts, nodeEnsurer: ensurerMock.Object, activeJobConfig: CreateActiveJobConfig());
        var store = Mock.Of<IArtefactStore>();
        var context = CreateImportContext(store);

        // Act
        await module.ImportAsync(context, CancellationToken.None);

        // Assert
        ensurerMock.Verify(e => e.ReplicateSourceTreeAsync(
            It.IsAny<ProjectMapping>(),
            It.IsAny<IArtefactStore>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IMigrationMetrics?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [TestMethod]
    public async Task ImportAsync_DoesNotCallEnsurer_WhenReplicateSourceTreeDisabled()
    {
        // Arrange
        var ensurerMock = new Mock<INodeEnsurer>(MockBehavior.Strict);
        var opts = new NodesModuleOptions
        {
            Enabled = true,
            ReplicateSourceTree = false
        };
        var module = CreateModule(opts, nodeEnsurer: ensurerMock.Object, activeJobConfig: CreateActiveJobConfig());
        var store = Mock.Of<IArtefactStore>();
        var context = CreateImportContext(store);

        // Act
        await module.ImportAsync(context, CancellationToken.None);

        // Assert — strict mock: no calls should have been made
        ensurerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenSourceTreeJsonMissing()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var module = CreateModule();
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

        var module = CreateModule();
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

        var module = CreateModule();
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(0, context.Errors.Count);
    }
}
