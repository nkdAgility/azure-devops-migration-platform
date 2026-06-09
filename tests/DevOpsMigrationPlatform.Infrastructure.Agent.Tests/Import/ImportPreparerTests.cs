// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class ImportPreparerTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PrepareAsync_ReturnsChangesRequired_WhenBlockingFindingsExist()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revisionPath]));

        IReadOnlyList<ImportFailureFinding> findings =
        [
            new(
                MissingAttachmentBinaryImportFailurePattern.Code,
                ImportFailureSeverity.Blocking,
                "WorkItems/2026-05-13/638827200000000000-42-0/attachment-a.png",
                "Missing attachment binary.",
                "Export attachment binaries."),
            new(
                FieldTransformCompatibilityImportFailurePattern.Code,
                ImportFailureSeverity.Blocking,
                "FieldNotFound|MapState|Custom.State|Unknown",
                "Configured transform references a missing field.",
                "Update transform configuration.")
        ];

        var sut = new ImportPreparer(
            Options.Create(new WorkItemsModuleOptions()),
            "myorg",
            "ProjectA",
            [new StaticPattern(findings)]);

        var report = await sut.PrepareAsync(CreateContext(package.Object), CancellationToken.None);

        Assert.AreEqual(WorkItemsPrepareReadinessResult.ChangesRequired, report.Readiness);
        Assert.AreEqual(1, report.ResolvedCount);
        Assert.AreEqual(2, report.UnresolvedCount);
        Assert.AreEqual(1, report.ArtefactFindings.Count);
        Assert.AreEqual(1, report.FieldTransformFindings.Count);
        Assert.AreEqual(ArtefactFindingType.Attachment, report.ArtefactFindings[0].ItemType);
        Assert.AreEqual(FieldTransformFindingStatus.FieldNotFound, report.FieldTransformFindings[0].Status);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.AreEqual(2, report.ImportReadinessReport.Findings.Count);
        Assert.IsFalse(report.ImportReadinessReport.IsReadyForImport);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PrepareAsync_ReturnsReady_WhenNoFindingsExist()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revisionPath]));

        var sut = new ImportPreparer(
            Options.Create(new WorkItemsModuleOptions()),
            "myorg",
            "ProjectA",
            [new StaticPattern([])]);

        var report = await sut.PrepareAsync(CreateContext(package.Object), CancellationToken.None);

        Assert.AreEqual(WorkItemsPrepareReadinessResult.Ready, report.Readiness);
        Assert.AreEqual(1, report.ResolvedCount);
        Assert.AreEqual(0, report.UnresolvedCount);
        Assert.AreEqual(0, report.ArtefactFindings.Count);
        Assert.AreEqual(0, report.FieldTransformFindings.Count);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.IsTrue(report.ImportReadinessReport.IsReadyForImport);
        Assert.AreEqual(0, report.ImportReadinessReport.Findings.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PrepareAsync_ReturnsReadyAndWarningClassification_WhenOnlyWarningFindingsExist()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revisionPath]));

        IReadOnlyList<ImportFailureFinding> findings =
        [
            new(
                "WORKITEMS_PREPARE_UNRESOLVED_IDENTITY",
                ImportFailureSeverity.Warning,
                "fallback@source.example",
                "No explicit identity mapping was found.",
                "Add an explicit mapping.")
        ];

        var sut = new ImportPreparer(
            Options.Create(new WorkItemsModuleOptions()),
            "myorg",
            "ProjectA",
            [new StaticPattern(findings)]);

        var report = await sut.PrepareAsync(CreateContext(package.Object), CancellationToken.None);

        Assert.AreEqual(WorkItemsPrepareReadinessResult.Ready, report.Readiness);
        Assert.AreEqual(1, report.UnresolvedCount);
        Assert.AreEqual(PrepareIssueSeverity.Warning, report.UnresolvedItems[0].Severity);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.AreEqual(0, report.ImportReadinessReport.BlockingCount);
        Assert.AreEqual(1, report.ImportReadinessReport.WarningCount);
        Assert.IsTrue(report.ImportReadinessReport.IsReadyForImport);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PrepareAsync_AggregatesFindingsAcrossAllPatterns()
    {
        var revisionPath = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revisionPath]));

        var patternA = new TrackingPattern(
        [
            new(
                MissingAttachmentBinaryImportFailurePattern.Code,
                ImportFailureSeverity.Blocking,
                "WorkItems/2026-05-13/638827200000000000-42-0/attachment-a.png",
                "Missing attachment binary.",
                "Export attachment binaries.")
        ]);
        var patternB = new TrackingPattern(
        [
            new(
                FieldTransformCompatibilityImportFailurePattern.Code,
                ImportFailureSeverity.Warning,
                "FieldNotFound|MapState|Custom.State|Unknown",
                "Configured transform references a missing field.",
                "Update transform configuration.")
        ]);

        var sut = new ImportPreparer(
            Options.Create(new WorkItemsModuleOptions()),
            "myorg",
            "ProjectA",
            [patternA, patternB]);

        var report = await sut.PrepareAsync(CreateContext(package.Object), CancellationToken.None);

        Assert.AreEqual(1, patternA.InvocationCount);
        Assert.AreEqual(1, patternB.InvocationCount);
        Assert.AreEqual(2, report.FailureFindings.Count);
        Assert.IsNotNull(report.ImportReadinessReport);
        Assert.AreEqual(2, report.ImportReadinessReport.Findings.Count);
    }

    private static PrepareContext CreateContext(IPackageAccess package)
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

    private static async IAsyncEnumerable<string> EnumerateManyAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            yield return path;
        }

        await Task.CompletedTask;
    }

    private sealed class StaticPattern(IReadOnlyList<ImportFailureFinding> findings) : IImportFailurePattern
    {
        public string PatternCode => "TEST";

        public Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
            ImportFailurePatternContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ImportFailureFinding>>(findings);
    }

    private sealed class TrackingPattern(IReadOnlyList<ImportFailureFinding> findings) : IImportFailurePattern
    {
        public int InvocationCount { get; private set; }

        public string PatternCode => "TRACKING";

        public Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
            ImportFailurePatternContext context,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(findings);
        }
    }
}
