// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class WorkItemsModulePrepareTests
{
    [TestMethod]
    public async Task PrepareAsync_WritesBlockingFindings_WhenAttachmentAndImageBinariesAreMissing()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var revisionJson = JsonSerializer.Serialize(CreateRevisionWithArtefacts());
        string? writtenReport = null;

        var artefactStore = new Mock<IArtefactStore>(MockBehavior.Strict);
        artefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns(EnumerateSingleAsync(revisionPath));
        artefactStore
            .Setup(s => s.ReadAsync(revisionPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        artefactStore
            .Setup(s => s.ExistsAsync("WorkItems/2026-05-13/638827200000000000-42-0/attachment-a.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        artefactStore
            .Setup(s => s.ExistsAsync("WorkItems/2026-05-13/638827200000000000-42-0/image-a.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        artefactStore
            .Setup(s => s.WriteAsync("WorkItems/prepare-report.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, payload, _) => writtenReport = payload)
            .Returns(Task.CompletedTask);

        var module = CreateModule();
        var context = CreatePrepareContext(artefactStore.Object);

        await module.PrepareAsync(context, CancellationToken.None);

        Assert.IsNotNull(writtenReport);
        var report = JsonSerializer.Deserialize<PrepareReport>(writtenReport!);
        Assert.IsNotNull(report);
        Assert.AreEqual(1, report.ResolvedCount);
        Assert.AreEqual(2, report.UnresolvedCount);
        Assert.AreEqual(WorkItemsPrepareReadinessResult.ChangesRequired, report.Readiness);
        Assert.AreEqual(2, report.FailureFindings.Count);
        Assert.AreEqual(2, report.ArtefactFindings.Count);
        Assert.AreEqual(0, report.FieldTransformFindings.Count);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.AreEqual(2, report.ImportReadinessReport.Findings.Count);
        Assert.AreEqual(2, report.ImportReadinessReport.BlockingCount);
        Assert.AreEqual(0, report.ImportReadinessReport.WarningCount);
        Assert.IsFalse(report.ImportReadinessReport.IsReadyForImport);
        Assert.AreEqual(2, report.ImportReadinessReport.ArtefactFindings.Count);
        Assert.AreEqual(0, report.ImportReadinessReport.FieldTransformFindings.Count);
        CollectionAssert.AreEquivalent(
            new[] { ArtefactFindingType.Attachment, ArtefactFindingType.EmbeddedImage },
            report.ArtefactFindings.Select(i => i.ItemType).ToArray());
        CollectionAssert.AreEquivalent(
            new[]
            {
                "WorkItems/2026-05-13/638827200000000000-42-0/attachment-a.png",
                "WorkItems/2026-05-13/638827200000000000-42-0/image-a.png"
            },
            report.UnresolvedItems.Select(i => i.Key).ToArray());
        Assert.IsTrue(report.UnresolvedItems.All(i => i.Severity == PrepareIssueSeverity.Blocking));

        artefactStore.VerifyAll();
    }

    [TestMethod]
    public async Task PrepareAsync_WritesResolvedReport_WhenRevisionArtefactsArePresent()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var revisionJson = JsonSerializer.Serialize(CreateRevisionWithArtefacts());
        string? writtenReport = null;

        var artefactStore = new Mock<IArtefactStore>(MockBehavior.Strict);
        artefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns(EnumerateSingleAsync(revisionPath));
        artefactStore
            .Setup(s => s.ReadAsync(revisionPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        artefactStore
            .Setup(s => s.ExistsAsync("WorkItems/2026-05-13/638827200000000000-42-0/attachment-a.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        artefactStore
            .Setup(s => s.ExistsAsync("WorkItems/2026-05-13/638827200000000000-42-0/image-a.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        artefactStore
            .Setup(s => s.WriteAsync("WorkItems/prepare-report.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, payload, _) => writtenReport = payload)
            .Returns(Task.CompletedTask);

        var module = CreateModule();
        var context = CreatePrepareContext(artefactStore.Object);

        await module.PrepareAsync(context, CancellationToken.None);

        Assert.IsNotNull(writtenReport);
        var report = JsonSerializer.Deserialize<PrepareReport>(writtenReport!);
        Assert.IsNotNull(report);
        Assert.AreEqual(1, report.ResolvedCount);
        Assert.AreEqual(0, report.UnresolvedCount);
        Assert.AreEqual(WorkItemsPrepareReadinessResult.Ready, report.Readiness);
        Assert.AreEqual(0, report.FailureFindings.Count);
        Assert.AreEqual(0, report.ArtefactFindings.Count);
        Assert.AreEqual(0, report.FieldTransformFindings.Count);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.AreEqual(0, report.ImportReadinessReport.Findings.Count);
        Assert.AreEqual(0, report.ImportReadinessReport.BlockingCount);
        Assert.AreEqual(0, report.ImportReadinessReport.WarningCount);
        Assert.IsTrue(report.ImportReadinessReport.IsReadyForImport);
        Assert.AreEqual(0, report.ImportReadinessReport.ArtefactFindings.Count);
        Assert.AreEqual(0, report.ImportReadinessReport.FieldTransformFindings.Count);

        artefactStore.VerifyAll();
    }

    [TestMethod]
    public async Task PrepareAsync_WritesFieldTransformFinding_WhenConfiguredFieldIsMissingFromExportedRevisions()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        string? writtenReport = null;

        var artefactStore = new Mock<IArtefactStore>(MockBehavior.Strict);
        artefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns(EnumerateSingleAsync(revisionPath));
        artefactStore
            .Setup(s => s.WriteAsync("WorkItems/prepare-report.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, payload, _) => writtenReport = payload)
            .Returns(Task.CompletedTask);

        var module = CreateModule(
        [
            new TestImportFailurePattern([
                new ImportFailureFinding(
                    "WORKITEMS_PREPARE_FIELD_TRANSFORM_COMPATIBILITY",
                    ImportFailureSeverity.Blocking,
                    "FieldNotFound|MapState|Custom.State|Unknown",
                    "Configured transform references a missing field.",
                    "Update the transform configuration.")
            ])
        ]);
        var context = CreatePrepareContext(artefactStore.Object);

        await module.PrepareAsync(context, CancellationToken.None);

        Assert.IsNotNull(writtenReport);
        var report = JsonSerializer.Deserialize<PrepareReport>(writtenReport!);
        Assert.IsNotNull(report);
        Assert.AreEqual(WorkItemsPrepareReadinessResult.ChangesRequired, report.Readiness);
        Assert.AreEqual(1, report.UnresolvedCount);
        Assert.AreEqual(1, report.FieldTransformFindings.Count);
        Assert.AreEqual(FieldTransformFindingStatus.FieldNotFound, report.FieldTransformFindings[0].Status);
        Assert.AreEqual("Custom.State", report.FieldTransformFindings[0].FieldName);
        Assert.AreEqual("MapState", report.FieldTransformFindings[0].TransformRule);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.AreEqual(1, report.ImportReadinessReport.Findings.Count);
        Assert.AreEqual(1, report.ImportReadinessReport.BlockingCount);
        Assert.AreEqual(0, report.ImportReadinessReport.WarningCount);
        Assert.IsFalse(report.ImportReadinessReport.IsReadyForImport);
        Assert.AreEqual(0, report.ImportReadinessReport.ArtefactFindings.Count);
        Assert.AreEqual(1, report.ImportReadinessReport.FieldTransformFindings.Count);

        artefactStore.VerifyAll();
    }

    private static WorkItemRevision CreateRevisionWithArtefacts() =>
        new()
        {
            WorkItemId = 42,
            RevisionIndex = 0,
            ChangedDate = DateTimeOffset.UtcNow,
            Attachments =
            [
                new AttachmentMetadata { OriginalName = "attachment-a.png", RelativePath = "attachment-a.png", Sha256 = "abc", Size = 10 }
            ],
            EmbeddedImages =
            [
                new EmbeddedImageMetadata { OriginalUrl = "https://example/image-a.png", RelativePath = "image-a.png", Extension = "png", Sha256 = "def", Size = 10 }
            ]
        };

    private static WorkItemsModule CreateModule(IEnumerable<IImportFailurePattern>? importFailurePatterns = null)
    {
        var sourceEndpoint = new Mock<ISourceEndpointInfo>();
        sourceEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        sourceEndpoint.SetupGet(s => s.Url).Returns("https://source.example");
        sourceEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new WorkItemsModule(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            NullLogger<WorkItemsModule>.Instance,
            Options.Create(new WorkItemsModuleOptions()),
            sourceEndpoint.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance,
            Mock.Of<IWorkItemImportTargetFactory>(),
            Mock.Of<IWorkItemResolutionStrategyFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IRevisionFolderProcessorFactory>(),
            targetEndpoint.Object,
            importFailurePatterns: importFailurePatterns);
    }

    private static PrepareContext CreatePrepareContext(IArtefactStore store)
    {
        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new PrepareContext
        {
            Job = new Job { JobId = "job-prepare-1", Kind = JobKind.Prepare },
            ArtefactStore = store,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>(),
            TargetEndpoint = targetEndpoint.Object
        };
    }

    private static async IAsyncEnumerable<string> EnumerateSingleAsync(string path)
    {
        yield return path;
        await Task.CompletedTask;
    }

    private sealed class TestImportFailurePattern(IReadOnlyList<ImportFailureFinding> findings) : IImportFailurePattern
    {
        public string PatternCode => "WORKITEMS_PREPARE_FIELD_TRANSFORM_COMPATIBILITY";

        public Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
            ImportFailurePatternContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(findings);
    }
}
