// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryConnectorLimitationsTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveMetaPath_CheckpointCursor_ThrowsContextRequiredContract()
    {
        var sut = new PackagePathRouter();

        var ex = Assert.ThrowsExactly<PackageOperationException>(
            () => sut.ResolveMetaPath(new PackageMetaContext(PackageMetaKind.CheckpointCursor)));

        Assert.AreEqual("PKG_META_KIND_CONTEXT_REQUIRED", ex.Code);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveMetaPath_ContinuationToken_ThrowsContextRequiredContract()
    {
        var sut = new PackagePathRouter();

        var ex = Assert.ThrowsExactly<PackageOperationException>(
            () => sut.ResolveMetaPath(new PackageMetaContext(PackageMetaKind.ContinuationToken)));

        Assert.AreEqual("PKG_META_KIND_CONTEXT_REQUIRED", ex.Code);
    }
}
