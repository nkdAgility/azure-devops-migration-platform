// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class WorkItemsModuleImportTests
{
    [TestMethod]
    public async Task ImportAsync_DispatchesNodeReadinessBeforeRevisionReplay()
    {
        var nodeReadinessDispatched = false;

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "Nodes/referenced-paths.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(CreatePayload(new ReferencedPathsArtifact([@"Source\Area"], []))));
        package
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "WorkItems" && c.IsCollectionRequest),
                It.IsAny<CancellationToken>()))
            .Returns(EnumerateFolderAsync("WorkItems/2026-05-13/638827200000000000-42-0/"));

        var nodeTranslationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        nodeTranslationTool
            .Setup(t => t.TranslatePath("System.AreaPath", @"Source\Area", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"Target\Area", false, true, false));

        var nodeCreator = new Mock<INodeCreator>(MockBehavior.Strict);
        nodeCreator
            .Setup(c => c.EnsureExistsAsync(ClassificationNodeType.Area, @"Target\Area", It.IsAny<CancellationToken>()))
            .Callback(() => nodeReadinessDispatched = true)
            .Returns(Task.CompletedTask);

        var nodeReadiness = new NodeReadinessOrchestrator(
            package.Object,
            nodeTranslationTool.Object,
            nodeCreator.Object,
            NullLogger<NodeReadinessOrchestrator>.Instance);

        var importTargetFactory = new Mock<IWorkItemImportTargetFactory>(MockBehavior.Strict);
        importTargetFactory
            .Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IWorkItemImportTarget>());

        var checkpointing = new Mock<ICheckpointingService>(MockBehavior.Strict);
        checkpointing
            .Setup(c => c.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        checkpointing
            .Setup(c => c.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var checkpointFactory = new Mock<ICheckpointingServiceFactory>(MockBehavior.Strict);
        checkpointFactory
            .Setup(f => f.Create(It.IsAny<IStateStore>()))
            .Returns(checkpointing.Object);

        var resolutionStrategy = new Mock<IWorkItemResolutionStrategy>(MockBehavior.Strict);
        resolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var resolutionStrategyFactory = new Mock<IWorkItemResolutionStrategyFactory>(MockBehavior.Strict);
        resolutionStrategyFactory
            .Setup(f => f.CreateAsync(
                It.IsAny<WorkItemResolutionStrategyOptions>(),
                It.IsAny<IWorkItemImportTarget>(),
                It.IsAny<MigrationEndpointOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolutionStrategy.Object);

        var idMapStore = new Mock<IIdMapStore>(MockBehavior.Strict);
        idMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        idMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<System.Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        idMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        idMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(42, 0, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        idMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var idMapStoreFactory = new Mock<IIdMapStoreFactory>(MockBehavior.Strict);
        idMapStoreFactory
            .Setup(f => f.CreateFromPackageUri("file:///package"))
            .Returns(idMapStore.Object);

        var revisionProcessor = new Mock<IRevisionFolderProcessor>(MockBehavior.Strict);
        revisionProcessor
            .Setup(p => p.ProcessAsync(
                "WorkItems/2026-05-13/638827200000000000-42-0/",
                It.IsAny<WorkItemsModuleExtensions>(),
                It.IsAny<string?>(),
                It.IsAny<IWorkItemResolutionStrategy>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.IsTrue(nodeReadinessDispatched, "Node readiness must execute before revision replay starts."))
            .Returns(Task.CompletedTask);

        var processorFactory = new Mock<IRevisionFolderProcessorFactory>(MockBehavior.Strict);
        processorFactory
            .Setup(f => f.Create(
                It.IsAny<IWorkItemImportTarget>(),
                idMapStore.Object,
                checkpointing.Object,
                It.IsAny<IIdentityLookupTool?>(),
                It.IsAny<IArtefactStore>(),
                It.IsAny<ProjectMapping?>()))
            .Returns(revisionProcessor.Object);

        var sourceEndpoint = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceEndpoint.SetupGet(s => s.Project).Returns("SourceProject");
        sourceEndpoint.SetupGet(s => s.Url).Returns("https://source.example");
        sourceEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        var targetEndpoint = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        targetEndpoint.SetupGet(s => s.Project).Returns("TargetProject");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        var module = new WorkItemsModule(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            NullLogger<WorkItemsModule>.Instance,
            Options.Create(new WorkItemsModuleOptions()),
            sourceEndpoint.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance,
            importTargetFactory.Object,
            resolutionStrategyFactory.Object,
            checkpointFactory.Object,
            idMapStoreFactory.Object,
            processorFactory.Object,
            targetEndpoint.Object,
            nodeReadinessOrchestrator: nodeReadiness,
            package: package.Object);

        await module.ImportAsync(
            new ImportContext
            {
                Job = new Job
                {
                    JobId = "job-import-1",
                    Kind = JobKind.Import,
                    Package = new JobPackage { PackageUri = "file:///package" },
                    Resume = new JobResume { Mode = ResumeMode.Auto }
                },
                ArtefactStore = Mock.Of<IArtefactStore>(),
                StateStore = Mock.Of<IStateStore>(),
                ProgressSink = Mock.Of<IProgressSink>()
            },
            CancellationToken.None);

        Assert.IsTrue(nodeReadinessDispatched);
        revisionProcessor.VerifyAll();
        nodeCreator.VerifyAll();
    }

    private static PackagePayload CreatePayload<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false), "application/json");
    }

    private static async IAsyncEnumerable<string> EnumerateFolderAsync(string folderPath)
    {
        yield return folderPath;
        await Task.CompletedTask;
    }
}
