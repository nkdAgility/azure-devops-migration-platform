// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryAdoptionAuditTests
{
    [TestMethod]
    public async Task BoundaryOperations_OnlyWriteRouterResolvedPaths()
    {
        var store = new AuditedArtefactStore();
        var active = new ActivePackageState
        {
            CurrentStore = store,
            CurrentJob = new DevOpsMigrationPlatform.Abstractions.Jobs.Job
            {
                JobId = "job-audit",
                Kind = DevOpsMigrationPlatform.Abstractions.Jobs.JobKind.Export
            }
        };
        var runId = active.CurrentRunId!;
        var router = new PackagePathRouter();
        var sut = new PackageBoundary(active, router, NullLogger<PackageBoundary>.Instance);

        var contentContext = new PackageContext("WorkItems/1/workitem.json");
        var metaContext = new PackageMetaContext(PackageMetaKind.ExecutionPlan, RelatedToRun: true);
        var logContext = new PackageLogContext(runId, PackageLogStream.Progress);

        await sut.PersistAsync(
            contentContext,
            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}"))),
            CancellationToken.None);
        await sut.PersistMetaAsync(
            metaContext,
            new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"done\":true}"))),
            CancellationToken.None);
        await sut.AppendLogAsync(
            logContext,
            new PackageLogPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"msg\":\"ok\"}\n"))),
            CancellationToken.None);

        var expected = new HashSet<string>
        {
            router.ResolveContentPath(contentContext),
            router.ResolveMetaPath(metaContext),
            router.ResolveMetaPath(metaContext, runId, runAudit: true),
            router.ResolveLogPath(logContext)
        };

        var actual = store.Writes.Concat(store.Appends).ToHashSet();
        CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList());
    }

    private sealed class AuditedArtefactStore : IArtefactStore
    {
        private readonly Dictionary<string, string> _content = new();
        public List<string> Writes { get; } = new();
        public List<string> Appends { get; } = new();

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_content.ContainsKey(path));

        public Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_content.TryGetValue(path, out var value) ? value : null);

        public Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Writes.Add(path);
            _content[path] = content;
            return Task.CompletedTask;
        }

        public Task AppendAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Appends.Add(path);
            _content[path] = _content.TryGetValue(path, out var existing) ? existing + content : content;
            return Task.CompletedTask;
        }

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
            => throw new System.NotSupportedException();

        public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
            => throw new System.NotSupportedException();

        public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var path in _content.Keys.Where(k => k.StartsWith(prefix, System.StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }

            await Task.CompletedTask;
        }

        public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)
            => throw new System.NotSupportedException();
    }
}
