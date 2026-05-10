// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
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

/// <summary>
/// Tests for the node replication and pre-collection logic now inlined in <see cref="NodesOrchestrator"/>.
/// </summary>
[TestClass]
public class NodeEnsurerTests
{
    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");

    private static (NodesOrchestrator sut, Mock<INodeCreator> creatorMock, Mock<IArtefactStore> storeMock, Mock<IStateStore> stateMock)
        CreateOrchestrator(
            NodeTranslationOptions? opts = null,
            INodeTranslationTool? tool = null,
            string? referencedPathsJson = null,
            string? sourceTreeJson = null)
    {
        opts ??= new NodeTranslationOptions { Enabled = true, AutoCreateNodes = true };
        tool ??= CreatePassThroughTool(opts);

        var creatorMock = new Mock<INodeCreator>(MockBehavior.Loose);
        creatorMock.Setup(c => c.EnsureExistsAsync(It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        var optionsMonitor = new Mock<IOptionsMonitor<NodeTranslationOptions>>();
        optionsMonitor.SetupGet(o => o.CurrentValue).Returns(opts);
        var package = PackageTestFactory.CreateLooseMock();
        package.Setup(p => p.RequestAsync(It.Is<PackageContext>(c => c.ContentKind == "Nodes/referenced-paths.json"), It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(referencedPathsJson));
        package.Setup(p => p.RequestAsync(It.Is<PackageContext>(c => c.ContentKind == "Nodes/source-tree.json"), It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(sourceTreeJson));
        package.Setup(p => p.RequestAsync(It.Is<PackageContext>(c => c.ContentKind == NodeReplicationProgress.StateKey), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));

        var orchestrator = new NodesOrchestrator(
            NullLogger<NodesOrchestrator>.Instance,
            tool,
            creatorMock.Object,
            optionsMonitor.Object,
            package: package.Object);

        return (orchestrator, creatorMock, storeMock, stateMock);
    }

    private static INodeTranslationTool CreatePassThroughTool(NodeTranslationOptions? opts = null)
    {
        opts ??= new NodeTranslationOptions
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
        var (sut, creatorMock, storeMock, _) = CreateOrchestrator(referencedPathsJson: json);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Area,
            It.IsAny<string>(),

            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_AutoCreateNodesDisabled_SkipsAllNodes()
    {
        var opts = new NodeTranslationOptions { Enabled = true, AutoCreateNodes = false };
        var json = BuildReferencedPathsJson(new[] { @"TargetProject\Team A" });
        var (sut, creatorMock, storeMock, _) = CreateOrchestrator(opts: opts, referencedPathsJson: json);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),

            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_NoArtifact_DoesNotThrow()
    {
        var (sut, creatorMock, storeMock, _) = CreateOrchestrator(referencedPathsJson: null);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),

            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task EnsureReferencedPathsAsync_EmptyPaths_DoesNotCallEnsure()
    {
        var json = BuildReferencedPathsJson(new List<string>());
        var (sut, creatorMock, storeMock, _) = CreateOrchestrator(referencedPathsJson: json);

        await sut.EnsureReferencedPathsAsync(DefaultMapping, storeMock.Object, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(),
            It.IsAny<string>(),

            It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- ReplicateSourceTreeAsync (called via ImportAsync) ---

    private static string BuildSourceTreeJson(
        IReadOnlyList<string>? areaNodes = null,
        IReadOnlyList<IterationNodeEntry>? iterNodes = null)
    {
        var snapshot = new ClassificationTreeSnapshot(
            areaNodes ?? new List<string>(),
            iterNodes ?? new List<IterationNodeEntry>());
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static ImportContext CreateImportContext(IArtefactStore store, IStateStore stateStore)
    {
        return new ImportContext
        {
            Job = new Job { Kind = JobKind.Import },
            ArtefactStore = store,
            StateStore = stateStore,
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_ResumesAndSkipsAlreadyReplicatedNodes()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var treeJson = BuildSourceTreeJson(areaNodes: new[] { @"SourceProject\Team A" });
        var creatorMock = new Mock<INodeCreator>(MockBehavior.Loose);
        creatorMock.Setup(c => c.EnsureExistsAsync(It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        var tool = CreatePassThroughTool(opts);
        var optionsMonitor = new Mock<IOptionsMonitor<NodeTranslationOptions>>();
        optionsMonitor.SetupGet(o => o.CurrentValue).Returns(opts);
        var package = PackageTestFactory.CreateLooseMock();
        package.Setup(p => p.RequestAsync(It.Is<PackageContext>(c => c.ContentKind == "Nodes/source-tree.json"), It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(treeJson));
        package.Setup(p => p.RequestAsync(It.Is<PackageContext>(c => c.ContentKind == "Nodes/referenced-paths.json"), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));
        package.Setup(p => p.RequestAsync(It.Is<PackageContext>(c => c.ContentKind == NodeReplicationProgress.StateKey), It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(progressJson));
        var orchestrator = new NodesOrchestrator(NullLogger<NodesOrchestrator>.Instance, tool, creatorMock.Object, optionsMonitor.Object, package: package.Object);

        var sourceEndpoint = Mock.Of<ISourceEndpointInfo>(e => e.Project == "SourceProject");
        var targetEndpoint = Mock.Of<ITargetEndpointInfo>(e => e.Project == "TargetProject");
        var context = CreateImportContext(storeMock.Object, stateMock.Object);

        await orchestrator.ImportAsync(context, sourceEndpoint, targetEndpoint, null, true, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_SetsIterationDatesWhenProvided()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var start = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var finish = new DateTimeOffset(2024, 1, 28, 0, 0, 0, TimeSpan.Zero);
        var treeJson = BuildSourceTreeJson(
            iterNodes: new[] { new IterationNodeEntry(@"SourceProject\Sprint 1", start, finish, false) });

        var (sut, creatorMock, storeMock, stateMock) = CreateOrchestrator(opts: opts, sourceTreeJson: treeJson);
        creatorMock.Setup(c => c.SetIterationDatesAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sourceEndpoint = Mock.Of<ISourceEndpointInfo>(e => e.Project == "SourceProject");
        var targetEndpoint = Mock.Of<ITargetEndpointInfo>(e => e.Project == "TargetProject");
        var context = CreateImportContext(storeMock.Object, stateMock.Object);

        await sut.ImportAsync(context, sourceEndpoint, targetEndpoint, null, true, CancellationToken.None);

        creatorMock.Verify(c => c.SetIterationDatesAsync(
            It.IsAny<string>(),
            It.Is<DateTimeOffset?>(d => d.HasValue),
            It.Is<DateTimeOffset?>(d => d.HasValue),

            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_DoesNotSetDatesForNullDates()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var treeJson = BuildSourceTreeJson(
            iterNodes: new[] { new IterationNodeEntry(@"SourceProject\Sprint 2", null, null, false) });

        var (sut, creatorMock, storeMock, stateMock) = CreateOrchestrator(opts: opts, sourceTreeJson: treeJson);
        creatorMock.Setup(c => c.SetIterationDatesAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sourceEndpoint = Mock.Of<ISourceEndpointInfo>(e => e.Project == "SourceProject");
        var targetEndpoint = Mock.Of<ITargetEndpointInfo>(e => e.Project == "TargetProject");
        var context = CreateImportContext(storeMock.Object, stateMock.Object);

        await sut.ImportAsync(context, sourceEndpoint, targetEndpoint, null, true, CancellationToken.None);

        creatorMock.Verify(c => c.SetIterationDatesAsync(
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_SetIterationDatesFails_LogsWarningAndContinues()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var start = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var finish = new DateTimeOffset(2024, 1, 28, 0, 0, 0, TimeSpan.Zero);
        var treeJson = BuildSourceTreeJson(
            iterNodes: new[] { new IterationNodeEntry(@"SourceProject\Sprint 1", start, finish, false) });

        var (sut, creatorMock, storeMock, stateMock) = CreateOrchestrator(opts: opts, sourceTreeJson: treeJson);
        creatorMock.Setup(c => c.SetIterationDatesAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        var sourceEndpoint = Mock.Of<ISourceEndpointInfo>(e => e.Project == "SourceProject");
        var targetEndpoint = Mock.Of<ITargetEndpointInfo>(e => e.Project == "TargetProject");
        var context = CreateImportContext(storeMock.Object, stateMock.Object);

        // Must not throw
        await sut.ImportAsync(context, sourceEndpoint, targetEndpoint, null, true, CancellationToken.None);

        // Node should still have been created
        creatorMock.Verify(c => c.EnsureExistsAsync(
            ClassificationNodeType.Iteration, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReplicateSourceTreeAsync_NoArtifact_LogsWarningAndDoesNotThrow()
    {
        var opts = new NodeTranslationOptions { Enabled = true };
        var (sut, creatorMock, storeMock, stateMock) = CreateOrchestrator(opts: opts, sourceTreeJson: null);

        var sourceEndpoint = Mock.Of<ISourceEndpointInfo>(e => e.Project == "SourceProject");
        var targetEndpoint = Mock.Of<ITargetEndpointInfo>(e => e.Project == "TargetProject");
        var context = CreateImportContext(storeMock.Object, stateMock.Object);

        await sut.ImportAsync(context, sourceEndpoint, targetEndpoint, null, true, CancellationToken.None);

        creatorMock.Verify(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ValueTask<PackagePayload?> ToPayload(string? json)
    {
        if (json is null)
            return ValueTask.FromResult<PackagePayload?>(null);

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));
    }
}
