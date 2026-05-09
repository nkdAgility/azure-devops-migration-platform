// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryErrorContractTests
{
    [TestMethod]
    public async Task RequestAsync_NoActiveStore_ThrowsPackageOperationExceptionWithStableCode()
    {
        var sut = new PackageBoundary(
            new ActivePackageState(),
            new PackagePathRouter(),
            NullLogger<PackageBoundary>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<PackageOperationException>(
            () => sut.RequestAsync(new PackageContext("analysis/dependencies.csv"), CancellationToken.None).AsTask());

        Assert.AreEqual("PKG_STORE_UNAVAILABLE", ex.Code);
    }

    [TestMethod]
    public async Task RequestAsync_UnsupportedContentKind_ThrowsPackageValidationExceptionWithStableCode()
    {
        var sut = new PackageBoundary(
            new ActivePackageState { CurrentStore = new InMemoryArtefactStore() },
            new PackagePathRouter(),
            NullLogger<PackageBoundary>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<PackageValidationException>(
            () => sut.RequestAsync(new PackageContext("dependencies"), CancellationToken.None).AsTask());

        Assert.AreEqual("PKG_CONTENT_KIND_UNSUPPORTED", ex.Code);
    }

    [TestMethod]
    public async Task AppendLogAsync_MissingRunId_ThrowsPackageValidationExceptionWithStableCode()
    {
        var sut = new PackageBoundary(
            new ActivePackageState { CurrentStore = new InMemoryArtefactStore() },
            new PackagePathRouter(),
            NullLogger<PackageBoundary>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<PackageValidationException>(
            () => sut.AppendLogAsync(
                new PackageLogContext("", PackageLogStream.Progress),
                new PackageLogPayload(new System.IO.MemoryStream()),
                CancellationToken.None).AsTask());

        Assert.AreEqual("PKG_RUN_ID_REQUIRED", ex.Code);
    }
}

