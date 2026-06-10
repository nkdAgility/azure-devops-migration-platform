// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryAdoptionAuditTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BoundaryOperations_OnlyWriteRouterResolvedPaths()
    {
        var store = new InMemoryPackageAccess();
        var (sut, active) = ActivePackageTestFactory.Create(store, "job-audit", DevOpsMigrationPlatform.Abstractions.Jobs.JobKind.Export);
        var runId = active.CurrentRunId!;
        var router = new PackagePathRouter();

        var contentContext = new PackageContentContext(PackageContentKind.Artefact, "test-org", "test-project", "WorkItems", Address: new TestPackageAddress("1/workitem.json"));
        var metaContext = new PackageMetaContext(PackageMetaKind.ExecutionPlan, RelatedToRun: true);
        var logContext = new PackageLogContext(runId, PackageLogStream.Progress);

        await sut.PersistContentAsync(
            contentContext,
            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}"))),
            CancellationToken.None);
        await sut.PersistMetaAsync(
            metaContext,
            new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"done\":true}"))),
            CancellationToken.None);
        await sut.AppendLogAsync(
            logContext,
            new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"ok\"}\n"))),
            CancellationToken.None);

        var expected = new HashSet<string>
        {
            router.ResolveContentPath(contentContext),
            router.ResolveMetaPath(metaContext),
            router.ResolveMetaPath(metaContext, runId, runAudit: true),
            router.ResolveLogPath(logContext)
        };

        foreach (var relativePath in expected)
        {
            Assert.IsTrue(File.Exists(Path.Combine(store.Root, relativePath.Replace('/', Path.DirectorySeparatorChar))), relativePath);
        }
    }
}

