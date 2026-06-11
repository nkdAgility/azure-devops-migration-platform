// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackagePathRouterTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveMetaPath_MigrationConfig_UsesAuthoritativePath()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveMetaPath(new PackageMetaContext(PackageMetaKind.MigrationConfig));

        Assert.AreEqual(PackagePathTestHelper.MigrationConfigFileName, path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveLogPath_Progress_UsesRunLogsFolder()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveLogPath(new PackageLogContext("20260509-120000", PackageLogStream.Progress));

        Assert.AreEqual(".migration/runs/20260509-120000/logs/progress.ndjson", path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_RootDependencies_UsesPackageRootCsv()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveIndexPath(new PackageIndexContext("dependencies.csv"));
        Assert.AreEqual("dependencies.csv", path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_OrgDependencies_UsesOrgScopedCsv()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveIndexPath(new PackageIndexContext("dependencies.csv", Organisation: "acme"));
        Assert.AreEqual("acme/dependencies.csv", path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_ProjectDependencies_UsesProjectScopedCsv()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveIndexPath(new PackageIndexContext("dependencies.csv", Organisation: "acme", Project: "myproject"));
        Assert.AreEqual("acme/myproject/dependencies.csv", path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_RootDependencyGraphArtefacts_UseStructuralRootPaths()
    {
        var sut = new PackagePathRouter();
        Assert.AreEqual(
            "discovery-project-dependencies.csv",
            sut.ResolveIndexPath(new PackageIndexContext("discovery-project-dependencies.csv")));
        Assert.AreEqual(
            "discovery-project-dependencies.md",
            sut.ResolveIndexPath(new PackageIndexContext("discovery-project-dependencies.md")));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_ProjectDependencyGraph_UsesProjectScopedMarkdown()
    {
        var sut = new PackagePathRouter();
        var path = sut.ResolveIndexPath(new PackageIndexContext("dependency-graph.md", Organisation: "org", Project: "ProjectA"));
        Assert.AreEqual("org/ProjectA/dependency-graph.md", path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_ProjectScoped_WithoutOrganisation_Throws()
    {
        var sut = new PackagePathRouter();
        var ex = Assert.ThrowsExactly<PackageValidationException>(
            () => sut.ResolveIndexPath(new PackageIndexContext("dependencies.csv", Project: "myproject")));
        Assert.AreEqual("PKG_INDEX_SCOPE_REQUIRED", ex.Code);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveIndexPath_PathInFileName_ThrowsValidationException()
    {
        var sut = new PackagePathRouter();
        var ex = Assert.ThrowsExactly<PackageValidationException>(
            () => sut.ResolveIndexPath(new PackageIndexContext("sub/dependencies.csv")));
        Assert.AreEqual("PKG_INDEX_FILENAME_INVALID", ex.Code);
    }

    private sealed class TestAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath { get; } = relativePath;
    }
}
