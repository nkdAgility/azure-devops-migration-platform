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
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class WorkItemTypeValidatorTests
{
    [TestMethod]
    public async Task EvaluateAsync_ReturnsBlockingFindings_WhenDistinctExportedTypesAreMissingOnTarget()
    {
        const string revisionA = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        const string revisionB = "WorkItems/2026-05-13/638827200000000001-43-0/revision.json";
        const string revisionC = "WorkItems/2026-05-13/638827200000000002-44-0/revision.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revisionA, revisionB, revisionC]));
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                PackagePayload? payload = path switch
                {
                    revisionA => CreatePayload(new WorkItemRevision
                    {
                        Fields = [new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Bug" }]
                    }),
                    revisionB => CreatePayload(new WorkItemRevision
                    {
                        Fields = [new WorkItemField { ReferenceName = "System.WorkItemType", Value = "bug" }]
                    }),
                    revisionC => CreatePayload(new WorkItemRevision
                    {
                        Fields = [new WorkItemField { ReferenceName = "System.WorkItemType", Value = "User Story" }]
                    }),
                    _ => null
                };

                return ValueTask.FromResult(payload);
            });

        var target = new Mock<IWorkItemTypeReadinessTarget>(MockBehavior.Strict);
        target
            .Setup(t => t.WorkItemTypeExistsAsync("Bug", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        target
            .Setup(t => t.WorkItemTypeExistsAsync("User Story", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var targetFactory = new Mock<IWorkItemTypeReadinessTargetFactory>(MockBehavior.Strict);
        targetFactory
            .Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(target.Object);

        var sut = new WorkItemTypeValidator(targetFactory.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(1, findings.Count);
        Assert.AreEqual(WorkItemTypeValidator.Code, findings[0].PatternCode);
        Assert.AreEqual(ImportFailureSeverity.Blocking, findings[0].Severity);
        Assert.AreEqual("Bug", findings[0].EvidenceKey);

        targetFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        target.Verify(t => t.WorkItemTypeExistsAsync("Bug", It.IsAny<CancellationToken>()), Times.Once);
        target.Verify(t => t.WorkItemTypeExistsAsync("User Story", It.IsAny<CancellationToken>()), Times.Once);
        target.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task EvaluateAsync_IgnoresInvalidPayloadAndMissingTypeFieldValues()
    {
        const string revisionA = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        const string revisionB = "WorkItems/2026-05-13/638827200000000001-43-0/revision.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revisionA, revisionB]));
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                if (path == revisionA)
                {
                    return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{bad-json}"))));
                }

                if (path == revisionB)
                {
                    return ValueTask.FromResult<PackagePayload?>(CreatePayload(new WorkItemRevision
                    {
                        Fields = [new WorkItemField { ReferenceName = "System.WorkItemType", Value = "  " }]
                    }));
                }

                return ValueTask.FromResult<PackagePayload?>(null);
            });

        var target = new Mock<IWorkItemTypeReadinessTarget>(MockBehavior.Strict);
        var targetFactory = new Mock<IWorkItemTypeReadinessTargetFactory>(MockBehavior.Strict);

        var sut = new WorkItemTypeValidator(targetFactory.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(0, findings.Count);
        targetFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        target.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task EvaluateAsync_DisposesTarget_WhenFactoryReturnsDisposableTarget()
    {
        const string revision = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revision]));
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(
                CreatePayload(new WorkItemRevision
                {
                    Fields = [new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Bug" }]
                })));

        var disposableTarget = new DisposableWorkItemTypeReadinessTarget();
        var targetFactory = new Mock<IWorkItemTypeReadinessTargetFactory>(MockBehavior.Strict);
        targetFactory
            .Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(disposableTarget);

        var sut = new WorkItemTypeValidator(targetFactory.Object);

        _ = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.IsTrue(disposableTarget.IsDisposed);
        targetFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PrepareContext CreatePrepareContext(IPackageAccess package)
    {
        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new PrepareContext
        {
            Job = new Job { JobId = "job-prepare-types", Kind = JobKind.Prepare },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>(),
            TargetEndpoint = targetEndpoint.Object
        };
    }

    private static async IAsyncEnumerable<string> EnumerateManyAsync(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            yield return value;
        }

        await Task.CompletedTask;
    }

    private static PackagePayload CreatePayload(WorkItemRevision revision)
    {
        var json = JsonSerializer.Serialize(revision);
        return new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private sealed class DisposableWorkItemTypeReadinessTarget : IWorkItemTypeReadinessTarget, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
