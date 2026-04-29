using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

[TestClass]
public class NodeEnsurerTests
{
    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");
    private static readonly MigrationEndpointOptions DefaultEndpoint = new SimulatedEndpointOptions();

    private static (NodeEnsurer sut, Mock<INodeCreator> creatorMock, Mock<IArtefactStore> storeMock, Mock<IStateStore> stateMock)
        CreateEnsurer(
            NodeTranslationOptions? opts = null,
            INodeTranslationTool? tool = null,
            string? referencedPathsJson = null,
            string? sourceTreeJson = null)
    {
        opts ??= new NodeTranslationOptions { Enabled = true, AutoCreateNodes = true };
        tool ??= CreatePassThroughTool();

        var creatorMock = new Mock<INodeCreator>(MockBehavior.Loose);
        creatorMock.Setup(c => c.EnsureExistsAsync(It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(referencedPathsJson);
        storeMock.Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceTreeJson);

        var stateMock = new Mock<IStateStore>(MockBehavior.Loose);
        stateMock.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        stateMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ensurer = new NodeEnsurer(
            Options.Create(opts),
            tool,
            creatorMock.Object,
            NullLogger<NodeEnsurer>.Instance);

        return (ensurer, creatorMock, storeMock, stateMock);
    }

    private static INodeTranslationTool CreatePassThroughTool()
    {
        var opts = new NodeTranslationOptions
        {
            Enabled = true,
            AreaPathMappings = [],
            IterationPathMappings = []
        };
        return new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);
    }

    private static string BuildReferencedPathsJson(IReadOnlyList<string> areaPaths, IReadOnlyList<string>? iterPaths = null)
    {
        var artifact = new ReferencedPathsArtifact(areaPaths, iterPaths ?? new List<string>());
        return JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_WithAreaPath_CallsEnsureExists()
    {
        var json = BuildReferencedPathsJson(new[] { @"TargetProject\Team A" });
        var (sut, creatorMock, storeMock, _) = CreateEnsurer(referencedPathsJson: json);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Area,
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_AutoCreateNodesDisabled_SkipsAllNodes()
    {
        var opts = new NodeTranslationOptions { Enabled = true, AutoCreateNodes = false };
        var json = BuildReferencedPathsJson(new[] { @"TargetProject\Team A" });
        var (sut, creatorMock, storeMock, _) = CreateEnsurer(opts: opts, referencedPathsJson: json);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_NoArtifact_DoesNotThrow()
    {
        var (sut, creatorMock, storeMock, _) = CreateEnsurer(referencedPathsJson: null);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_EmptyPaths_DoesNotCallEnsure()
    {
        var json = BuildReferencedPathsJson(new List<string>());
        var (sut, creatorMock, storeMock, _) = CreateEnsurer(referencedPathsJson: json);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- ReplicateSourceTreeAsync ---

    private static string BuildSourceTreeJson(
        IReadOnlyList<string>? areaNodes = null,
        IReadOnlyList<IterationNodeEntry>? iterNodes = null)
    {
        var snapshot = new ClassificationTreeSnapshot(
            areaNodes ?? new List<string>(),
            iterNodes ?? new List<IterationNodeEntry>());
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_ResumesAndSkipsAlreadyReplicatedNodes()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var treeJson = BuildSourceTreeJson(areaNodes: new[] { @"SourceProject\Team A" });
        var creatorMock = new Mock<INodeCreator>(MockBehavior.Loose);
        creatorMock.Setup(c => c.EnsureExistsAsync(It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>())).ReturnsAsync(treeJson);
        storeMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        // Pre-load checkpoint containing the translated path
        var progress = new NodeReplicationProgress();
        progress.ReplicatedPaths.Add(@"TargetProject\Team A");
        var progressJson = JsonSerializer.Serialize(progress, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var stateMock = new Mock<IStateStore>(MockBehavior.Loose);
        stateMock.Setup(s => s.ReadAsync(NodeReplicationProgress.StateKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(progressJson);
        stateMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = CreatePassThroughTool();
        var ensurer = new NodeEnsurer(Options.Create(opts), tool, creatorMock.Object, NullLogger<NodeEnsurer>.Instance);

        await ensurer.ReplicateSourceTreeAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, stateMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_SetsIterationDatesWhenProvided()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var start = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var finish = new DateTimeOffset(2024, 1, 28, 0, 0, 0, TimeSpan.Zero);
        var treeJson = BuildSourceTreeJson(
            iterNodes: new[] { new IterationNodeEntry(@"SourceProject\Sprint 1", start, finish, false) });

        var (sut, creatorMock, storeMock, stateMock) = CreateEnsurer(opts: opts, sourceTreeJson: treeJson);
        creatorMock.Setup(c => c.SetIterationDatesAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.ReplicateSourceTreeAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, stateMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.SetIterationDatesAsync(
            It.IsAny<string>(),
            It.Is<DateTimeOffset?>(d => d.HasValue),
            It.Is<DateTimeOffset?>(d => d.HasValue),
            It.IsAny<MigrationEndpointOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_DoesNotSetDatesForNullDates()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var treeJson = BuildSourceTreeJson(
            iterNodes: new[] { new IterationNodeEntry(@"SourceProject\Sprint 2", null, null, false) });

        var (sut, creatorMock, storeMock, stateMock) = CreateEnsurer(opts: opts, sourceTreeJson: treeJson);
        creatorMock.Setup(c => c.SetIterationDatesAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.ReplicateSourceTreeAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, stateMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.SetIterationDatesAsync(
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_SetIterationDatesFails_LogsWarningAndContinues()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var start = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var finish = new DateTimeOffset(2024, 1, 28, 0, 0, 0, TimeSpan.Zero);
        var treeJson = BuildSourceTreeJson(
            iterNodes: new[] { new IterationNodeEntry(@"SourceProject\Sprint 1", start, finish, false) });

        var (sut, creatorMock, storeMock, stateMock) = CreateEnsurer(opts: opts, sourceTreeJson: treeJson);
        creatorMock.Setup(c => c.SetIterationDatesAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        // Must not throw
        await sut.ReplicateSourceTreeAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, stateMock.Object, CancellationToken.None);

        // Node should still have been created
        creatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Iteration, It.IsAny<string>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_NoArtifact_LogsWarningAndDoesNotThrow()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var (sut, creatorMock, storeMock, stateMock) = CreateEnsurer(opts: opts, sourceTreeJson: null);

        await sut.ReplicateSourceTreeAsync(DefaultMapping, DefaultEndpoint, storeMock.Object, stateMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
