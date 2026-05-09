// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryTests
{
    [TestMethod]
    public async Task PersistMetaAsync_RelatedToRun_WritesAuthoritativeAndAuditCopies()
    {
        var store = new InMemoryArtefactStore();
        var active = new ActivePackageState
        {
            CurrentStore = store,
            CurrentJob = new Job { JobId = "job-1", Kind = JobKind.Export }
        };
        var sut = new PackageBoundary(active, new PackagePathRouter(), NullLogger<PackageBoundary>.Instance);
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
        var store = new InMemoryArtefactStore();
        var active = new ActivePackageState { CurrentStore = store };
        var sut = new PackageBoundary(active, new PackagePathRouter(), NullLogger<PackageBoundary>.Instance);
        var payload = new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"hello\"}\n")));
        var context = new PackageLogContext("20260509-120500", PackageLogStream.Diagnostics);

        await sut.AppendLogAsync(context, payload, CancellationToken.None);

        var logPath = ".migration/runs/20260509-120500/logs/diagnostics.ndjson";
        Assert.AreEqual("{\"msg\":\"hello\"}\n", await store.ReadAsync(logPath, CancellationToken.None));
    }

    private sealed class InMemoryArtefactStore : IArtefactStore
    {
        private readonly Dictionary<string, string> _files = new(System.StringComparer.OrdinalIgnoreCase);

        public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.TryGetValue(path, out var value) ? value : null);

        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.ContainsKey(path));

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);
        public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var key in _files.Keys)
            {
                if (key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    yield return key;
                await Task.Yield();
            }
        }
        public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = _files.TryGetValue(path, out var existing) ? existing + content : content;
            return Task.CompletedTask;
        }
    }
}

