// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageMetaRoutingTests
{
    [TestMethod]
    public async Task PersistMetaAsync_RelatedToRunTrue_WritesAuthoritativeAndRunAuditCopies()
    {
        var store = new InMemoryArtefactStore();
        var active = new ActivePackageState
        {
            CurrentStore = store,
            CurrentJob = new Job { JobId = "job-1", Kind = JobKind.Export }
        };
        var sut = new PackageBoundary(active, new PackagePathRouter(), NullLogger<PackageBoundary>.Instance);
        var payload = new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"mode\":\"Export\"}")));

        await sut.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig, RelatedToRun: true),
            payload,
            CancellationToken.None);

        Assert.AreEqual("{\"mode\":\"Export\"}", await store.ReadAsync(".migration/migration-config.json", CancellationToken.None));
        Assert.AreEqual("{\"mode\":\"Export\"}", await store.ReadAsync(".migration/runs/" + active.CurrentRunId + "/audit/migration-config.json", CancellationToken.None));
    }

    [TestMethod]
    public async Task PersistMetaAsync_RelatedToRunFalse_WritesOnlyAuthoritativeCopy()
    {
        var store = new InMemoryArtefactStore();
        var active = new ActivePackageState
        {
            CurrentStore = store,
            CurrentJob = new Job { JobId = "job-2", Kind = JobKind.Export }
        };
        var sut = new PackageBoundary(active, new PackagePathRouter(), NullLogger<PackageBoundary>.Instance);
        var payload = new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"mode\":\"Export\"}")));

        await sut.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig, RelatedToRun: false),
            payload,
            CancellationToken.None);

        Assert.AreEqual("{\"mode\":\"Export\"}", await store.ReadAsync(".migration/migration-config.json", CancellationToken.None));
        Assert.IsNull(await store.ReadAsync(".migration/runs/" + active.CurrentRunId + "/audit/migration-config.json", CancellationToken.None));
    }
}

