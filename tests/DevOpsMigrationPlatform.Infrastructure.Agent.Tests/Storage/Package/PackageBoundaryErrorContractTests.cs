// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryErrorContractTests
{
    [TestMethod]
    public async Task RequestAsync_NoActiveStore_ThrowsPackageOperationExceptionWithStableCode()
    {
        var sut = new ActivePackageAccess(
            new ActivePackageState(),
            new PackagePathRouter(),
            NullLogger<ActivePackageAccess>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<PackageOperationException>(
            () => sut.RequestContentAsync(
                new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress("analysis/dependencies.csv")),
                CancellationToken.None).AsTask());

        Assert.AreEqual("PKG_STORE_UNAVAILABLE", ex.Code);
    }

    [TestMethod]
    public async Task RequestAsync_MissingRoute_ThrowsPackageValidationExceptionWithStableCode()
    {
        var (sut, _) = ActivePackageTestFactory.Create(new InMemoryPackageAccess());

        var ex = await Assert.ThrowsExactlyAsync<PackageValidationException>(
            () => sut.RequestContentAsync(new PackageContentContext(PackageContentKind.Artefact), CancellationToken.None).AsTask());

        Assert.AreEqual("PKG_ROUTE_REQUIRED", ex.Code);
    }

    [TestMethod]
    public async Task AppendLogAsync_MissingRunId_ThrowsPackageValidationExceptionWithStableCode()
    {
        var (sut, _) = ActivePackageTestFactory.Create(new InMemoryPackageAccess());

        var ex = await Assert.ThrowsExactlyAsync<PackageValidationException>(
            () => sut.AppendLogAsync(
                new PackageLogContext("", PackageLogStream.Progress),
                new PackageLogPayload(new System.IO.MemoryStream()),
                CancellationToken.None).AsTask());

        Assert.AreEqual("PKG_RUN_ID_REQUIRED", ex.Code);
    }
}


