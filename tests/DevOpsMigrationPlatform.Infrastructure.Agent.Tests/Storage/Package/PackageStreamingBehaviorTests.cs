// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageStreamingBehaviorTests
{
    [TestMethod]
    public async Task RequestAsync_DoesNotEnumerateOrBufferAcrossPackage()
    {
        var store = new Mock<IArtefactStore>(MockBehavior.Strict);
        store.Setup(s => s.ReadAsync("analysis/dependencies.csv", It.IsAny<CancellationToken>()))
            .ReturnsAsync("id,name");
        store.Setup(s => s.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new AssertFailedException("EnumerateAsync should not be used by package content request routing."));

        var sut = new PackageBoundary(
            new ActivePackageState { CurrentStore = store.Object },
            new PackagePathRouter(),
            NullLogger<PackageBoundary>.Instance);

        var payload = await sut.RequestAsync(new PackageContext("analysis/dependencies.csv"), CancellationToken.None);

        Assert.IsNotNull(payload);
    }

    [TestMethod]
    public async Task PersistAsync_DoesNotEnumerateOrSortAcrossPackage()
    {
        var store = new Mock<IArtefactStore>(MockBehavior.Strict);
        store.Setup(s => s.WriteAsync("analysis/dependencies.csv", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new AssertFailedException("EnumerateAsync should not be used by package content persist routing."));

        var sut = new PackageBoundary(
            new ActivePackageState { CurrentStore = store.Object },
            new PackagePathRouter(),
            NullLogger<PackageBoundary>.Instance);

        await sut.PersistAsync(
            new PackageContext("analysis/dependencies.csv"),
            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("id,name")), "text/csv"),
            CancellationToken.None);

        store.Verify(s => s.WriteAsync("analysis/dependencies.csv", "id,name", It.IsAny<CancellationToken>()), Times.Once);
    }
}

