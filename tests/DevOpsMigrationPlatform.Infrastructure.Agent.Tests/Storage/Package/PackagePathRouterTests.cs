// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackagePathRouterTests
{
    [TestMethod]
    public void ResolveMetaPath_MigrationConfig_UsesAuthoritativePath()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveMetaPath(new PackageMetaContext(PackageMetaKind.MigrationConfig));

        Assert.AreEqual(PackagePathTestHelper.MigrationConfigFileName, path);
    }

    [TestMethod]
    public void ResolveLogPath_Progress_UsesRunLogsFolder()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveLogPath(new PackageLogContext("20260509-120000", PackageLogStream.Progress));

        Assert.AreEqual(".migration/runs/20260509-120000/logs/progress.ndjson", path);
    }

    [TestMethod]
    public void ResolveContentPath_AbsoluteAddress_ThrowsValidationException()
    {
        var sut = new PackagePathRouter();
        var context = new PackageContentContext(
            PackageContentKind.Artefact,
            Organisation: "org",
            Project: "project",
            Module: "WorkItems",
            Address: new TestAddress("/absolute/path.json"));

        var ex = Assert.ThrowsExactly<PackageValidationException>(() => sut.ResolveContentPath(context));
        Assert.AreEqual("PKG_ADDRESS_INVALID", ex.Code);
    }

    [TestMethod]
    public void ResolveContentPath_EscapingAddress_ThrowsValidationException()
    {
        var sut = new PackagePathRouter();
        var context = new PackageContentContext(
            PackageContentKind.Artefact,
            Organisation: "org",
            Project: "project",
            Module: "WorkItems",
            Address: new TestAddress("../escape.json"));

        var ex = Assert.ThrowsExactly<PackageValidationException>(() => sut.ResolveContentPath(context));
        Assert.AreEqual("PKG_ADDRESS_INVALID", ex.Code);
    }

    private sealed class TestAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath { get; } = relativePath;
    }
}

