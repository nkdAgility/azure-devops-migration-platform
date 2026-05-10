// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageLogRotationContinuityTests
{
    [TestMethod]
    public async Task AppendLogAsync_WhenRunIdChanges_WritesEachStreamToItsOwnRunFile()
    {
        var store = new InMemoryArtefactStore();
        var active = new ActivePackageState { CurrentStore = store };
        var sut = new ActivePackageAccess(active, new PackagePathRouter(), NullLogger<ActivePackageAccess>.Instance);

        await sut.AppendLogAsync(
            new PackageLogContext("run-a", PackageLogStream.Diagnostics),
            new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"a1\"}\n"))),
            CancellationToken.None);

        await sut.AppendLogAsync(
            new PackageLogContext("run-b", PackageLogStream.Diagnostics),
            new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"b1\"}\n"))),
            CancellationToken.None);

        Assert.AreEqual("{\"msg\":\"a1\"}\n", await store.ReadAsync(".migration/runs/run-a/logs/diagnostics.ndjson", CancellationToken.None));
        Assert.AreEqual("{\"msg\":\"b1\"}\n", await store.ReadAsync(".migration/runs/run-b/logs/diagnostics.ndjson", CancellationToken.None));
    }
}
