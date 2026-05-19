// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageMetaRoutingTests
{
    [TestMethod]
    public async Task PersistMetaAsync_RelatedToRunTrue_WritesAuthoritativeAndRunAuditCopies()
    {
        var store = new InMemoryPackageAccess();
        var (sut, active) = ActivePackageTestFactory.Create(store, "job-1", JobKind.Export);
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
        var store = new InMemoryPackageAccess();
        var (sut, active) = ActivePackageTestFactory.Create(store, "job-2", JobKind.Export);
        var payload = new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"mode\":\"Export\"}")));

        await sut.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig, RelatedToRun: false),
            payload,
            CancellationToken.None);

        Assert.AreEqual("{\"mode\":\"Export\"}", await store.ReadAsync(".migration/migration-config.json", CancellationToken.None));
        Assert.IsNull(await store.ReadAsync(".migration/runs/" + active.CurrentRunId + "/audit/migration-config.json", CancellationToken.None));
    }
}


