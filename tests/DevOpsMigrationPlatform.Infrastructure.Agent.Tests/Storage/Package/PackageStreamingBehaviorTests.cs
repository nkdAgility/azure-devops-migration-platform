// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageStreamingBehaviorTests
{
    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    [TestMethod]
    public async Task RequestAsync_DoesNotEnumerateOrBufferAcrossPackage()
    {
        var store = new Mock<ITestArtefactStore>(MockBehavior.Strict);
        store.Setup(s => s.ReadAsync("test-org/test-project/TestModule/analysis/dependencies.csv", It.IsAny<CancellationToken>()))
            .ReturnsAsync("id,name");
        store.Setup(s => s.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new AssertFailedException("EnumerateAsync should not be used by package content request routing."));

        var sut = PackageTestFactory.CreateDelegatingMock(store.Object).Object;

        var payload = await sut.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, "test-org", "test-project", "TestModule", Address: new TestPackageAddress("analysis/dependencies.csv")),
            CancellationToken.None);

        Assert.IsNotNull(payload);
    }

    [TestMethod]
    public async Task PersistAsync_DoesNotEnumerateOrSortAcrossPackage()
    {
        var store = new Mock<ITestArtefactStore>(MockBehavior.Strict);
        store.Setup(s => s.WriteAsync("test-org/test-project/TestModule/analysis/dependencies.csv", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new AssertFailedException("EnumerateAsync should not be used by package content persist routing."));

        var sut = PackageTestFactory.CreateDelegatingMock(store.Object).Object;

        await sut.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, "test-org", "test-project", "TestModule", Address: new TestPackageAddress("analysis/dependencies.csv")),
            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("id,name")), "text/csv"),
            CancellationToken.None);

        store.Verify(s => s.WriteAsync("test-org/test-project/TestModule/analysis/dependencies.csv", "id,name", It.IsAny<CancellationToken>()), Times.Once);
    }
}
