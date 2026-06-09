// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public class ReferencedPathsFromWorkItemsStrategyTests
{
    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CollectDistinctPathsAsync_CollectsDistinctAreaAndIterationPaths_FromExportedRevisions()
    {
        var package = PackageTestFactory.CreateLooseMock();
        await PersistRevisionAsync(
            package.Object,
            "WorkItems/2026-05-01/638816832000000000-42-0/revision.json",
            @"Source\Team A",
            @"Source\Sprint 1");
        await PersistRevisionAsync(
            package.Object,
            "WorkItems/2026-05-02/638817696000000000-42-1/revision.json",
            @"source\team a",
            @"Source\Sprint 2");
        await PersistRevisionAsync(
            package.Object,
            "WorkItems/2026-05-03/638818560000000000-100-0/revision.json",
            @"Source\Team B",
            @"Source\Sprint 2");

        var strategy = new ReferencedPathsFromWorkItemsStrategy(
            package.Object,
            NullLogger<ReferencedPathsFromWorkItemsStrategy>.Instance,
            "test-org",
            "test-project");

        var result = await strategy.CollectDistinctPathsAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { @"Source\Team A", @"Source\Team B" },
            result.AreaPaths.ToArray());
        CollectionAssert.AreEqual(
            new[] { @"Source\Sprint 1", @"Source\Sprint 2" },
            result.IterationPaths.ToArray());
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CollectDistinctPathsAsync_IgnoresCommentFoldersAndMissingRevisionContent()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.EnumerateContentAsync(
                It.IsAny<PackageContentContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EnumeratePathsAsync(
                "WorkItems/2026-05-01/638816832000000000-42-0/",
                "WorkItems/2026-05-01/638816832000000000-42-c7/",
                "WorkItems/2026-05-01/638816832000000001-42-1/"));
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "WorkItems/2026-05-01/638816832000000000-42-0/revision.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(BuildRevisionPayload(@"Source\Area", @"Source\Iteration")));
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == "WorkItems/2026-05-01/638816832000000001-42-1/revision.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));

        package.Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(false));
        package.Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Stream?>(null));
        package.Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) => new ValueTask<PackageMetaResult>(new PackageMetaResult(context.Kind.ToString(), null)));
        package.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package.Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package.Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package.Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package.Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var strategy = new ReferencedPathsFromWorkItemsStrategy(
            package.Object,
            NullLogger<ReferencedPathsFromWorkItemsStrategy>.Instance,
            "test-org",
            "test-project");

        var result = await strategy.CollectDistinctPathsAsync(CancellationToken.None);

        Assert.AreEqual(1, result.AreaPaths.Count);
        Assert.AreEqual(1, result.IterationPaths.Count);
    }

    private static async Task PersistRevisionAsync(
        IPackageAccess packageAccess,
        string path,
        string areaPath,
        string iterationPath)
    {
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 0,
            ChangedDate = System.DateTimeOffset.UtcNow,
            Fields =
            [
                new WorkItemField { ReferenceName = "System.AreaPath", Value = areaPath },
                new WorkItemField { ReferenceName = "System.IterationPath", Value = iterationPath }
            ]
        };

        var json = JsonSerializer.Serialize(revision, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var payload = new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false), "application/json");

        await packageAccess.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Organisation: "test-org", Project: "test-project", Module: "WorkItems", Address: new TestPackageAddress(path)),
            payload,
            CancellationToken.None);
    }

    private static PackagePayload BuildRevisionPayload(string areaPath, string iterationPath)
    {
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 0,
            ChangedDate = System.DateTimeOffset.UtcNow,
            Fields =
            [
                new WorkItemField { ReferenceName = "System.AreaPath", Value = areaPath },
                new WorkItemField { ReferenceName = "System.IterationPath", Value = iterationPath }
            ]
        };

        var json = JsonSerializer.Serialize(revision, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false), "application/json");
    }

    private static async IAsyncEnumerable<string> EnumeratePathsAsync(params string[] paths)
    {
        foreach (var path in paths)
            yield return path;
    }

    private static System.Collections.Generic.IReadOnlyList<string> SplitRouteSegments(string relativePath)
        => relativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
}
