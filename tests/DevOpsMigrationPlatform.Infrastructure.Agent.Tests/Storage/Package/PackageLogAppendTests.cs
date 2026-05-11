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
public sealed class PackageLogAppendTests
{
    [TestMethod]
    public async Task AppendLogAsync_ProgressStream_AppendsToProgressRunLog()
    {
        var store = new InMemoryArtefactStore();
        var sut = new ActivePackageAccess(
            new ActivePackageState { CurrentStore = store },
            new PackagePathRouter(),
            NullLogger<ActivePackageAccess>.Instance);

        await sut.AppendLogAsync(
            new PackageLogContext("20260509-120500", PackageLogStream.Progress),
            new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"hello\"}\n"))),
            CancellationToken.None);

        Assert.AreEqual(
            "{\"msg\":\"hello\"}\n",
            await store.ReadAsync(".migration/runs/20260509-120500/logs/progress.ndjson", CancellationToken.None));
    }

    [TestMethod]
    public async Task AppendLogAsync_DiagnosticsStream_AppendsToDiagnosticsRunLog()
    {
        var store = new InMemoryArtefactStore();
        var sut = new ActivePackageAccess(
            new ActivePackageState { CurrentStore = store },
            new PackagePathRouter(),
            NullLogger<ActivePackageAccess>.Instance);

        await sut.AppendLogAsync(
            new PackageLogContext("20260509-120500", PackageLogStream.Diagnostics),
            new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"hello\"}\n"))),
            CancellationToken.None);

        Assert.AreEqual(
            "{\"msg\":\"hello\"}\n",
            await store.ReadAsync(".migration/runs/20260509-120500/logs/diagnostics.ndjson", CancellationToken.None));
    }
}

