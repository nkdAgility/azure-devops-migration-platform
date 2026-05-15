// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryTests
{
    [TestMethod]
    public async Task ExistsAsync_ReturnsTrue_WhenContentPathExists()
    {
        var store = new InMemoryPackageAccess();
        await store.WriteAsync("WorkItems/entry.json", "{}", CancellationToken.None);
        var (sut, _) = ActivePackageTestFactory.Create(store);

        var exists = await sut.ContentExistsAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress("WorkItems/entry.json")),
            CancellationToken.None);

        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task EnumerateAsync_YieldsLexicographicStoreEntries()
    {
        var store = new InMemoryPackageAccess();
        await store.WriteAsync("WorkItems/b.json", "{}", CancellationToken.None);
        await store.WriteAsync("WorkItems/a.json", "{}", CancellationToken.None);
        var (sut, _) = ActivePackageTestFactory.Create(store);

        var entries = new List<string>();
        await foreach (var item in sut.EnumerateContentAsync(
            new PackageContentContext(PackageContentKind.Collection, Address: new TestPackageAddress("WorkItems")),
            CancellationToken.None))
            entries.Add(item);

        CollectionAssert.AreEquivalent(new[] { "WorkItems/a.json", "WorkItems/b.json" }, entries.ToArray());
    }

    [TestMethod]
    public async Task AppendAsync_AppendsContentToExistingFile()
    {
        var store = new InMemoryPackageAccess();
        await store.WriteAsync("Identities/descriptors.jsonl", "one\n", CancellationToken.None);
        var (sut, _) = ActivePackageTestFactory.Create(store);

        await sut.AppendContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress("Identities/descriptors.jsonl")),
            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("two\n"))),
            CancellationToken.None);

        Assert.AreEqual("one\ntwo\n", await store.ReadAsync("Identities/descriptors.jsonl", CancellationToken.None));
    }

    [TestMethod]
    public async Task PersistMetaAsync_RelatedToRun_WritesAuthoritativeAndAuditCopies()
    {
        var store = new InMemoryPackageAccess();
        var (sut, active) = ActivePackageTestFactory.Create(store, "job-1", JobKind.Export);
        var payload = new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"mode\":\"Export\"}")));

        await sut.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig, RelatedToRun: true),
            payload,
            CancellationToken.None);

        Assert.AreEqual("{\"mode\":\"Export\"}", await store.ReadAsync(".migration/migration-config.json", CancellationToken.None));
        Assert.AreEqual("{\"mode\":\"Export\"}", await store.ReadAsync(".migration/runs/" + active.CurrentRunId + "/audit/migration-config.json", CancellationToken.None));
    }

    [TestMethod]
    public async Task AppendLogAsync_AppendsToRunStream()
    {
        var store = new InMemoryPackageAccess();
        var (sut, active) = ActivePackageTestFactory.Create(store);
        var payload = new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"hello\"}\n")));
        var context = new PackageLogContext("20260509-120500", PackageLogStream.Diagnostics);

        await sut.AppendLogAsync(context, payload, CancellationToken.None);

        var logPath = ".migration/runs/20260509-120500/logs/diagnostics.ndjson";
        Assert.AreEqual("{\"msg\":\"hello\"}\n", await store.ReadAsync(logPath, CancellationToken.None));
    }

}


