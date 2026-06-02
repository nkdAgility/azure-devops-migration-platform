// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;

[TestClass]
public class IdentityMappingServiceTests
{
    private string _storeDir = null!;
    private FileSystemArtefactStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _storeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_storeDir);
        _store = new FileSystemArtefactStore(_storeDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_storeDir)) Directory.Delete(_storeDir, recursive: true);
    }

    private FileSystemIdentityMappingService MakeSut(
        Dictionary<string, string>? mappings = null,
        string fallback = "bot@target.example.com")
        => new(mappings ?? new(), fallback, PackageTestFactory.CreateDelegatingMock(_store).Object, "test-org", "test-project");

    [TestMethod]
    public void Resolve_WhenMappingExists_ReturnsMappedIdentity()
    {
        var sut = MakeSut(new() { ["alice@source.com"] = "alice@target.com" });
        Assert.AreEqual("alice@target.com", sut.Resolve("alice@source.com"));
    }

    [TestMethod]
    public void Resolve_WhenNoMapping_ReturnsFallback()
    {
        var sut = MakeSut(fallback: "fallback@target.com");
        Assert.AreEqual("fallback@target.com", sut.Resolve("unknown@source.com"));
    }

    [TestMethod]
    public void Resolve_WhenNoMapping_AddsToUnmappedList()
    {
        var sut = MakeSut();
        sut.Resolve("unknown@source.com");
        Assert.AreEqual(1, sut.UnmappedIdentities.Count);
        Assert.AreEqual("unknown@source.com", sut.UnmappedIdentities[0]);
    }

    [TestMethod]
    public void Resolve_SameUnmappedTwice_OnlyRecordedOnce()
    {
        var sut = MakeSut();
        sut.Resolve("x@source.com");
        sut.Resolve("x@source.com");
        Assert.AreEqual(1, sut.UnmappedIdentities.Count);
    }

    [TestMethod]
    public async Task FlushWarningsAsync_WritesLogFileForEachUnmapped()
    {
        var sut = MakeSut();
        sut.Resolve("a@src.com");
        sut.Resolve("b@src.com");

        await sut.FlushWarningsAsync(CancellationToken.None);

        var files = Directory.GetFiles(_storeDir, "*.log", System.IO.SearchOption.AllDirectories);
        Assert.AreEqual(2, files.Length);
    }

    [TestMethod]
    public async Task FlushWarningsAsync_ClearsUnmappedListAfterFlush()
    {
        var sut = MakeSut();
        sut.Resolve("x@src.com");
        await sut.FlushWarningsAsync(CancellationToken.None);
        Assert.AreEqual(0, sut.UnmappedIdentities.Count);
    }

    [TestMethod]
    public void Resolve_WhenMappingExistsForSomeAndNotOthers_FallsBackCorrectly()
    {
        var sut = MakeSut(
            new() { ["mapped@src.com"] = "mapped@tgt.com" },
            fallback: "bot@tgt.com");

        Assert.AreEqual("mapped@tgt.com", sut.Resolve("mapped@src.com"));
        Assert.AreEqual("bot@tgt.com", sut.Resolve("unmapped@src.com"));
    }
}
