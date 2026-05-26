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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.Logging.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class NodePathValidatorTests
{
    [TestMethod]
    public async Task EvaluateAsync_ReturnsBlockingFindings_WhenDistinctPathsAreMissingOnTarget()
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
                PackagePayload? payload = path switch
                {
                    revisionA => CreatePayload(new WorkItemRevision
                    {
                        Fields =
                        [
                            new WorkItemField { ReferenceName = "System.AreaPath", Value = @"Project\Team A" },
                            new WorkItemField { ReferenceName = "System.IterationPath", Value = @"Project\Sprint 1" }
                        ]
                    }),
                    revisionB => CreatePayload(new WorkItemRevision
                    {
                        Fields =
                        [
                            new WorkItemField { ReferenceName = "System.AreaPath", Value = @"Project\Team A" },
                            new WorkItemField { ReferenceName = "System.IterationPath", Value = @"Project\Sprint 2" }
                        ]
                    }),
                    _ => null
                };

                return ValueTask.FromResult(payload);
            });

        var nodeCreator = new Mock<INodeCreator>(MockBehavior.Strict);
        nodeCreator
            .Setup(n => n.NodeExistsAsync(ClassificationNodeType.Area, @"Project\Team A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        nodeCreator
            .Setup(n => n.NodeExistsAsync(ClassificationNodeType.Iteration, @"Project\Sprint 1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        nodeCreator
            .Setup(n => n.NodeExistsAsync(ClassificationNodeType.Iteration, @"Project\Sprint 2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new NodePathValidator(nodeCreator.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(2, findings.Count);
        Assert.IsTrue(findings.Any(f => f.EvidenceKey == @"System.AreaPath|Project\Team A"));
        Assert.IsTrue(findings.Any(f => f.EvidenceKey == @"System.IterationPath|Project\Sprint 1"));
        Assert.IsTrue(findings.All(f => f.PatternCode == NodePathValidator.Code));
        Assert.IsTrue(findings.All(f => f.Severity == ImportFailureSeverity.Blocking));
        nodeCreator.Verify(n => n.NodeExistsAsync(ClassificationNodeType.Area, @"Project\Team A", It.IsAny<CancellationToken>()), Times.Once);
        nodeCreator.Verify(n => n.NodeExistsAsync(ClassificationNodeType.Iteration, @"Project\Sprint 1", It.IsAny<CancellationToken>()), Times.Once);
        nodeCreator.Verify(n => n.NodeExistsAsync(ClassificationNodeType.Iteration, @"Project\Sprint 2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EvaluateAsync_IgnoresInvalidRevisionPayloads_AndEmptyNodeValues()
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
                    return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{invalid-json}"))));
                }

                if (path == revisionB)
                {
                    return ValueTask.FromResult<PackagePayload?>(CreatePayload(new WorkItemRevision
                    {
                        Fields =
                        [
                            new WorkItemField { ReferenceName = "System.AreaPath", Value = "  " },
                            new WorkItemField { ReferenceName = "System.IterationPath", Value = @"Project\Sprint 1" }
                        ]
                    }));
                }

                return ValueTask.FromResult<PackagePayload?>(null);
            });

        var nodeCreator = new Mock<INodeCreator>(MockBehavior.Strict);
        nodeCreator
            .Setup(n => n.NodeExistsAsync(ClassificationNodeType.Iteration, @"Project\Sprint 1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new NodePathValidator(nodeCreator.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(0, findings.Count);
        nodeCreator.Verify(n => n.NodeExistsAsync(ClassificationNodeType.Iteration, @"Project\Sprint 1", It.IsAny<CancellationToken>()), Times.Once);
        nodeCreator.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task EvaluateAsync_WithSimulatedNodeCreator_NormalizesSlashAndTrailingSeparatorPathsForExistingNodes()
    {
        const string revision = "WorkItems/2026-05-13/638827200000000000-42-0/revision.json";
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => EnumerateManyAsync([revision]));
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                if (path != revision)
                {
                    return ValueTask.FromResult<PackagePayload?>(null);
                }

                return ValueTask.FromResult<PackagePayload?>(CreatePayload(new WorkItemRevision
                {
                    Fields =
                    [
                        new WorkItemField { ReferenceName = "System.AreaPath", Value = "TargetProject/Platform/Backend/" },
                        new WorkItemField { ReferenceName = "System.IterationPath", Value = "TargetProject/Sprint 1/" }
                    ]
                }));
            });

        var nodeCreator = new SimulatedNodeCreator(
            NullLogger<SimulatedNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://example.dev.azure.com/target",
                Project = "TargetProject",
                ConnectorType = "Simulated"
            });
        await nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None);
        await nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, @"TargetProject\Sprint 1", CancellationToken.None);

        var sut = new NodePathValidator(nodeCreator);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(0, findings.Count);
    }

    private static PrepareContext CreatePrepareContext(IPackageAccess package)
    {
        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new PrepareContext
        {
            Job = new Job { JobId = "job-prepare-nodepaths", Kind = JobKind.Prepare },
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

    private sealed record TestTargetEndpointInfo : ITargetEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }

        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType
        };
    }
}
