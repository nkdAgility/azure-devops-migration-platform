// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class WorkItemsModulePrepareTests
{
    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    private static PackageContentContext ContentAt(string path)
        => new(PackageContentKind.Artefact, "test-org", "test-project", "WorkItems", Address: new TestPackageAddress(path));

    [TestMethod]
    public async Task PrepareAsync_WritesBlockingFindings_WhenAttachmentAndImageBinariesAreMissing()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var revisionRelativePath = revisionPath.Substring("WorkItems/".Length);
        var revisionJson = JsonSerializer.Serialize(CreateRevisionWithArtefacts());
        string? writtenReport = null;
        string? writtenReadinessReport = null;

        var package = PackageTestFactory.CreateLooseMock();
        CapturePrepareReports(package, s => writtenReport = s, s => writtenReadinessReport = s);
        package
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.IsCollectionRequest && c.Module == "WorkItems"),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateSingleAsync(revisionPath));
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Module == "WorkItems" && c.Address!.RelativePath == revisionRelativePath),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(revisionJson)))));

        var module = CreateModule();
        var context = CreatePrepareContext(package.Object);

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
        Assert.IsNotNull(writtenReadinessReport);
        var readinessReport = JsonSerializer.Deserialize<ImportReadinessReport>(writtenReadinessReport!);
        Assert.IsNotNull(readinessReport);
        Assert.IsFalse(readinessReport.IsReadyForImport);
        Assert.AreEqual(2, readinessReport.BlockingCount);
        Assert.AreEqual(0, readinessReport.WarningCount);
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
    }

    [TestMethod]
    public async Task PrepareAsync_WritesResolvedReport_WhenRevisionArtefactsArePresent()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var revisionRelativePath = revisionPath.Substring("WorkItems/".Length);
        var revisionJson = JsonSerializer.Serialize(CreateRevisionWithArtefacts());
        string? writtenReport = null;
        string? writtenReadinessReport = null;

        var package = PackageTestFactory.CreateLooseMock();
        CapturePrepareReports(package, s => writtenReport = s, s => writtenReadinessReport = s);
        package
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.IsCollectionRequest && c.Module == "WorkItems"),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateSingleAsync(revisionPath));
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Module == "WorkItems" && c.Address!.RelativePath == revisionRelativePath),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(revisionJson)))));
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var module = CreateModule();
        var context = CreatePrepareContext(package.Object);

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
        Assert.IsNotNull(writtenReadinessReport);
        var readinessReport = JsonSerializer.Deserialize<ImportReadinessReport>(writtenReadinessReport!);
        Assert.IsNotNull(readinessReport);
        Assert.IsTrue(readinessReport.IsReadyForImport);
        Assert.AreEqual(0, readinessReport.BlockingCount);
        Assert.AreEqual(0, readinessReport.WarningCount);
    }

    [TestMethod]
    public async Task PrepareAsync_WritesFieldTransformFinding_WhenConfiguredFieldIsMissingFromExportedRevisions()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var revisionRelativePath = revisionPath.Substring("WorkItems/".Length);
        string? writtenReport = null;
        string? writtenReadinessReport = null;

        var package = PackageTestFactory.CreateLooseMock();
        CapturePrepareReports(package, s => writtenReport = s, s => writtenReadinessReport = s);
        package
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.IsCollectionRequest && c.Module == "WorkItems"),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateSingleAsync(revisionPath));
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Module == "WorkItems" && c.Address!.RelativePath == revisionRelativePath),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{}")))));

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
        var context = CreatePrepareContext(package.Object);

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
        Assert.IsNotNull(writtenReadinessReport);
        var readinessReport = JsonSerializer.Deserialize<ImportReadinessReport>(writtenReadinessReport!);
        Assert.IsNotNull(readinessReport);
        Assert.IsFalse(readinessReport.IsReadyForImport);
        Assert.AreEqual(1, readinessReport.BlockingCount);
        Assert.AreEqual(0, readinessReport.WarningCount);
    }

    [TestMethod]
    public async Task PrepareAsync_ThrowsWrappedFailure_WhenPreparerEvaluationFails()
    {
        var package = PackageTestFactory.CreateLooseMock();
        var module = CreateModule(
        [
            new ThrowingImportFailurePattern(new InvalidOperationException("synthetic-prepare-failure"))
        ]);
        var context = CreatePrepareContext(package.Object);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => module.PrepareAsync(context, CancellationToken.None));

        StringAssert.Contains(exception.Message, "[WorkItems] Prepare phase dispatch failed.");
        Assert.IsNotNull(exception.InnerException);
        Assert.AreEqual("synthetic-prepare-failure", exception.InnerException.Message);
    }

    [TestMethod]
    public async Task PrepareAsync_PropagatesCancellation_WhenCancellationRequested()
    {
        var package = PackageTestFactory.CreateLooseMock();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var module = CreateModule(
        [
            new ThrowingImportFailurePattern(new OperationCanceledException(cts.Token))
        ]);
        var context = CreatePrepareContext(package.Object);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => module.PrepareAsync(context, cts.Token));
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
        sourceEndpoint.SetupGet(s => s.OrganisationSlug).Returns("test-org");

        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");
        targetEndpoint.SetupGet(s => s.OrganisationSlug).Returns("test-target-org");

        return new WorkItemsModule(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            NullLogger<WorkItemsModule>.Instance,
            Options.Create(new WorkItemsModuleOptions()),
            sourceEndpoint.Object,
            NullLogger<WorkItemsImportRuntime>.Instance,
            Mock.Of<IWorkItemTargetFactory>(),
            Mock.Of<IWorkItemResolutionStrategyFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IWorkItemResolutionProcessorFactory>(),
            targetEndpoint.Object,
            identityMappingService: Mock.Of<IIdentityMappingService>(),
            nodeTranslationTool: Mock.Of<INodeTranslationTool>(),
            fieldTransformTool: Mock.Of<IFieldTransformTool>(),
            importFailurePatterns: importFailurePatterns);
    }

    private static PrepareContext CreatePrepareContext(IPackageAccess package)
    {
        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new PrepareContext
        {
            Job = new Job { JobId = "job-prepare-1", Kind = JobKind.Prepare },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>(),
            TargetEndpoint = targetEndpoint.Object
        };
    }

    private static async IAsyncEnumerable<string> EnumerateSingleAsync(string path)
    {
        yield return path;
        await Task.CompletedTask;
    }

    private static void CapturePrepareReports(
        Mock<IPackageAccess> package,
        Action<string> setPrepareReport,
        Action<string> setReadinessReport)
    {
        package
            .Setup(p => p.PersistContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Module == "WorkItems" &&
                    c.Organisation == "test-org" &&
                    c.Project == "ProjectA" &&
                    c.Address!.RelativePath == "prepare-report.json"),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, false, 1024, true);
                setPrepareReport(reader.ReadToEnd());
                payload.Content.Position = 0;
            })
            .Returns(ValueTask.CompletedTask);

        package
            .Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.WorkItemsImportReadiness),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageMetaContext, PackageMetaPayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, false, 1024, true);
                setReadinessReport(reader.ReadToEnd());
                payload.Content.Position = 0;
            })
            .Returns(ValueTask.CompletedTask);
    }

    private sealed class TestImportFailurePattern(IReadOnlyList<ImportFailureFinding> findings) : IImportFailurePattern
    {
        public string PatternCode => "WORKITEMS_PREPARE_FIELD_TRANSFORM_COMPATIBILITY";

        public Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
            ImportFailurePatternContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(findings);
    }

    private sealed class ThrowingImportFailurePattern(Exception exception) : IImportFailurePattern
    {
        public string PatternCode => "THROWING";

        public Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
            ImportFailurePatternContext context,
            CancellationToken cancellationToken)
            => Task.FromException<IReadOnlyList<ImportFailureFinding>>(exception);
    }
}
