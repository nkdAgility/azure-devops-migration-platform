// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public class NodeReadinessOrchestratorTests
{
    [TestMethod]
    public async Task ExecuteAsync_ReferencedPaths_EnsuresTranslatedAreaAndIterationPaths()
    {
        var referenced = new ReferencedPathsArtifact(
            AreaPaths: [@"Source\Team A", @"Source\Team A"],
            IterationPaths: [@"Source\Sprint 1"]);

        var packageMock = CreatePackageMock(referencedPaths: referenced);
        var translationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        translationTool
            .Setup(t => t.TranslatePath("System.AreaPath", @"Source\Team A", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Team A", false, true, false));
        translationTool
            .Setup(t => t.TranslatePath("System.IterationPath", @"Source\Sprint 1", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Sprint 1", false, true, false));

        var creator = new Mock<INodeCreator>(MockBehavior.Strict);
        creator
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Target\Team A", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        creator
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Iteration, @"Target\Sprint 1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NodeReadinessOrchestrator(
            packageMock.Object,
            translationTool.Object,
            creator.Object,
            NullLogger<NodeReadinessOrchestrator>.Instance);

        await sut.ExecuteAsync(new ProjectMapping("Source", "Target"), replicateSourceTree: false, CancellationToken.None);

        creator.VerifyAll();
        creator.Verify(
            c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Target\Team A", It.IsAny<CancellationToken>()),
            Times.Once);
        creator.Verify(
            c => c.EnsureExistsAsync(ClassificationNodeType.Iteration, @"Target\Sprint 1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_ReferencedPaths_TranslatesBeforeCreatingNodes()
    {
        var referenced = new ReferencedPathsArtifact(
            AreaPaths: [@"Source\Area"],
            IterationPaths: [@"Source\Iteration"]);

        var packageMock = CreatePackageMock(referencedPaths: referenced);
        var sequence = new MockSequence();
        var translationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        translationTool
            .InSequence(sequence)
            .Setup(t => t.TranslatePath("System.AreaPath", @"Source\Area", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Area", false, true, false));

        var creator = new Mock<INodeCreator>(MockBehavior.Strict);
        creator
            .InSequence(sequence)
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Target\Area", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        translationTool
            .InSequence(sequence)
            .Setup(t => t.TranslatePath("System.IterationPath", @"Source\Iteration", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Iteration", false, true, false));
        creator
            .InSequence(sequence)
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Iteration, @"Target\Iteration", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NodeReadinessOrchestrator(
            packageMock.Object,
            translationTool.Object,
            creator.Object,
            NullLogger<NodeReadinessOrchestrator>.Instance);

        await sut.ExecuteAsync(new ProjectMapping("Source", "Target"), replicateSourceTree: false, CancellationToken.None);

        creator.Verify(c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Source\Area", It.IsAny<CancellationToken>()), Times.Never);
        creator.Verify(c => c.EnsureExistsAsync(ClassificationNodeType.Iteration, @"Source\Iteration", It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_ReplicateSourceTree_EnsuresSnapshotNodesAndIterationDates()
    {
        var sourceTree = new ClassificationTreeSnapshot(
            AreaNodes: [@"Source\Platform"],
            IterationNodes:
            [
                new IterationNodeEntry(
                    Path: @"Source\Sprint 2",
                    StartDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    FinishDate: new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
                    IsBacklogIteration: false)
            ]);

        var packageMock = CreatePackageMock(sourceTree: sourceTree);
        var translationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        translationTool
            .Setup(t => t.TranslatePath("System.AreaPath", @"Source\Platform", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Platform", false, true, false));
        translationTool
            .Setup(t => t.TranslatePath("System.IterationPath", @"Source\Sprint 2", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Sprint 2", false, true, false));

        var creator = new Mock<INodeCreator>(MockBehavior.Strict);
        creator
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Target\Platform", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        creator
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Iteration, @"Target\Sprint 2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        creator
            .Setup(c => c.SetIterationDatesAsync(
                @"Target\Sprint 2",
                sourceTree.IterationNodes[0].StartDate,
                sourceTree.IterationNodes[0].FinishDate,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NodeReadinessOrchestrator(
            packageMock.Object,
            translationTool.Object,
            creator.Object,
            NullLogger<NodeReadinessOrchestrator>.Instance);

        await sut.ExecuteAsync(new ProjectMapping("Source", "Target"), replicateSourceTree: true, CancellationToken.None);

        creator.VerifyAll();
    }

    [TestMethod]
    public async Task ExecuteAsync_ReplicateSourceTree_EnumeratesPackageForClassificationMetadata()
    {
        var sourceTree = new ClassificationTreeSnapshot(
            AreaNodes: [@"Source\Platform"],
            IterationNodes: []);

        var packageMock = new Mock<IPackageAccess>(MockBehavior.Strict);
        packageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes/referenced-paths.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));
        packageMock.Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "WorkItems"),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());
        packageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes/source-tree.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));
        packageMock.Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes"),
                It.IsAny<CancellationToken>()))
            .Returns(EnumerateAsync("Nodes/metadata/source-tree.json"));
        packageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes/metadata/source-tree.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(CreatePayload(sourceTree)));
        packageMock.Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(false));
        packageMock.Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<System.IO.Stream?>(null));
        packageMock.Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) => new ValueTask<PackageMetaResult>(new PackageMetaResult(context.Kind.ToString(), null)));
        packageMock.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        packageMock.Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        packageMock.Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        packageMock.Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        packageMock.Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var translationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        translationTool
            .Setup(t => t.TranslatePath("System.AreaPath", @"Source\Platform", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Platform", false, true, false));

        var creator = new Mock<INodeCreator>(MockBehavior.Strict);
        creator
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Target\Platform", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NodeReadinessOrchestrator(
            packageMock.Object,
            translationTool.Object,
            creator.Object,
            NullLogger<NodeReadinessOrchestrator>.Instance);

        await sut.ExecuteAsync(new ProjectMapping("Source", "Target"), replicateSourceTree: true, CancellationToken.None);

        creator.VerifyAll();
    }

    [TestMethod]
    public async Task ExecuteAsync_NoNodeArtifacts_DoesNothing()
    {
        var packageMock = CreatePackageMock();
        var translationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        var creator = new Mock<INodeCreator>(MockBehavior.Strict);

        var sut = new NodeReadinessOrchestrator(
            packageMock.Object,
            translationTool.Object,
            creator.Object,
            NullLogger<NodeReadinessOrchestrator>.Instance);

        await sut.ExecuteAsync(new ProjectMapping("Source", "Target"), replicateSourceTree: true, CancellationToken.None);

        translationTool.VerifyNoOtherCalls();
        creator.VerifyNoOtherCalls();
    }

    private static Mock<IPackageAccess> CreatePackageMock(
        ReferencedPathsArtifact? referencedPaths = null,
        ClassificationTreeSnapshot? sourceTree = null)
    {
        var mock = new Mock<IPackageAccess>(MockBehavior.Strict);
        mock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes/referenced-paths.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(CreatePayload(referencedPaths)));
        mock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes/source-tree.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(CreatePayload(sourceTree)));
        mock.Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(false));
        mock.Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());
        mock.Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<System.IO.Stream?>(null));
        mock.Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) => new ValueTask<PackageMetaResult>(new PackageMetaResult(context.Kind.ToString(), null)));
        mock.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        mock.Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        mock.Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        mock.Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        mock.Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        return mock;
    }

    private static PackagePayload? CreatePayload<T>(T artifact)
    {
        if (artifact is null)
            return null;

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json), writable: false), "application/json");
    }

    private static async IAsyncEnumerable<string> EmptyAsync()
    {
        yield break;
    }

    private static async IAsyncEnumerable<string> EnumerateAsync(params string[] paths)
    {
        foreach (var path in paths)
            yield return path;
    }
}
