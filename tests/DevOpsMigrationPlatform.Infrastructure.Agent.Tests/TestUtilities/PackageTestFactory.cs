// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

internal static class PackageTestFactory
{
    public static Mock<IPackageAccess> CreateLooseMock()
    {
        var contentStore = new Dictionary<string, byte[]>(System.StringComparer.OrdinalIgnoreCase);
        var metaStore = new Dictionary<string, byte[]>(System.StringComparer.OrdinalIgnoreCase);
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                if (!contentStore.TryGetValue(context.Address!.RelativePath, out var bytes))
                    return ValueTask.FromResult<PackagePayload?>(null);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false)));
            });
        package
            .Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                if (!contentStore.TryGetValue(context.Address!.RelativePath, out var bytes))
                    return ValueTask.FromResult<System.IO.Stream?>(null);
                return ValueTask.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(bytes, writable: false));
            });
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) =>
            {
                var key = ResolveMetaPath(context);
                if (!metaStore.TryGetValue(key, out var bytes))
                    return ValueTask.FromResult(new PackageMetaResult(key, null));
                return ValueTask.FromResult(new PackageMetaResult(key, new PackageMetaPayload(new System.IO.MemoryStream(bytes, writable: false))));
            });
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) => ValueTask.FromResult(contentStore.ContainsKey(context.Address!.RelativePath)));
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) => EnumerateByPrefixAsync(contentStore.Keys, context.Address!.RelativePath));
        package
            .Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, PackagePayload payload, CancellationToken _) =>
            {
                contentStore[context.Address!.RelativePath] = ReadAllBytes(payload.Content);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, System.IO.Stream payload, string? _, CancellationToken _) =>
            {
                contentStore[context.Address!.RelativePath] = ReadAllBytes(payload);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, PackageMetaPayload payload, CancellationToken _) =>
            {
                metaStore[ResolveMetaPath(context)] = ReadAllBytes(payload.Content);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.DeleteMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) =>
            {
                metaStore.Remove(ResolveMetaPath(context));
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, PackagePayload payload, CancellationToken _) =>
            {
                var incoming = ReadAllBytes(payload.Content);
                if (contentStore.TryGetValue(context.Address!.RelativePath, out var existing))
                    contentStore[context.Address!.RelativePath] = existing.Concat(incoming).ToArray();
                else
                    contentStore[context.Address!.RelativePath] = incoming;
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(It.IsAny<PackageMetaKind>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DbConnection>(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:")));
        package
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<IDisposable>(new NoOpLockHandle()));
        return package;
    }

    public static Mock<IPackageAccess> CreateDelegatingMock(IArtefactStore artefactStore, IStateStore? stateStore = null)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var content = IsStatePath(context.Address!.RelativePath) && stateStore is not null
                    ? await stateStore.ReadAsync(context.Address!.RelativePath, ct).ConfigureAwait(false)
                    : await artefactStore.ReadAsync(context.Address!.RelativePath, ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct)
                => IsStatePath(context.Address!.RelativePath) && stateStore is not null
                    ? new ValueTask<bool>(stateStore.ExistsAsync(context.Address!.RelativePath, ct))
                    : new ValueTask<bool>(artefactStore.ExistsAsync(context.Address!.RelativePath, ct)));
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct)
                => artefactStore.EnumerateAsync(NormalizeCollectionPrefix(context), ct));
        package
            .Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var stream = await artefactStore.ReadBinaryAsync(context.Address!.RelativePath, ct).ConfigureAwait(false);
                if (stream is null)
                    return null;
                if (stream.CanSeek)
                    stream.Position = 0;
                return stream;
            });
        package
            .Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                if (IsStatePath(context.Address!.RelativePath) && stateStore is not null)
                    await stateStore.WriteAsync(context.Address!.RelativePath, text, ct).ConfigureAwait(false);
                else
                    await artefactStore.WriteAsync(context.Address!.RelativePath, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, System.IO.Stream payload, string? _, CancellationToken ct) =>
            {
                var bytes = ReadAllBytes(payload);
                await artefactStore.WriteBinaryAsync(context.Address!.RelativePath, bytes, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                if (IsStatePath(context.Address!.RelativePath) && stateStore is not null)
                {
                    var existing = await stateStore.ReadAsync(context.Address!.RelativePath, ct).ConfigureAwait(false) ?? string.Empty;
                    await stateStore.WriteAsync(context.Address!.RelativePath, existing + text, ct).ConfigureAwait(false);
                }
                else
                {
                    await artefactStore.AppendAsync(context.Address!.RelativePath, text, ct).ConfigureAwait(false);
                }
            });
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, CancellationToken ct) =>
            {
                var key = ResolveMetaPath(context);
                if (stateStore is null)
                    return new PackageMetaResult(key, null);

                var content = await stateStore.ReadAsync(key, ct).ConfigureAwait(false);
                if (content is null)
                    return new PackageMetaResult(key, null);
                return new PackageMetaResult(key, new PackageMetaPayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json"));
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, PackageMetaPayload payload, CancellationToken ct) =>
            {
                if (stateStore is null)
                    return;
                var key = ResolveMetaPath(context);
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(key, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.DeleteMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, CancellationToken ct) =>
            {
                if (stateStore is null)
                    return;
                await stateStore.DeleteAsync(ResolveMetaPath(context), ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(It.IsAny<PackageMetaKind>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DbConnection>(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:")));
        package
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<IDisposable>(new NoOpLockHandle()));
        return package;
    }

    public static Mock<IPackageAccess> CreateStateDelegatingMock(IStateStore stateStore)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var content = await stateStore.ReadAsync(context.Address!.RelativePath, ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(context.Address!.RelativePath, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct) => new ValueTask<bool>(stateStore.ExistsAsync(context.Address!.RelativePath, ct)));
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, CancellationToken ct) =>
            {
                var key = ResolveMetaPath(context);
                var content = await stateStore.ReadAsync(key, ct).ConfigureAwait(false);
                if (content is null)
                    return new PackageMetaResult(key, null);
                return new PackageMetaResult(key, new PackageMetaPayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json"));
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, PackageMetaPayload payload, CancellationToken ct) =>
            {
                var key = ResolveMetaPath(context);
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(key, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.DeleteMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken ct) => new ValueTask(stateStore.DeleteAsync(ResolveMetaPath(context), ct)));
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());
        package
            .Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<System.IO.Stream?>(null));
        package
            .Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var existing = await stateStore.ReadAsync(context.Address!.RelativePath, ct).ConfigureAwait(false) ?? string.Empty;
                var incoming = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(context.Address!.RelativePath, existing + incoming, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.OpenNativeDatabaseAsync(It.IsAny<PackageMetaKind>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DbConnection>(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:")));
        package
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<IDisposable>(new NoOpLockHandle()));
        return package;
    }

    private static byte[] ReadAllBytes(System.IO.Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
        using var ms = new System.IO.MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static async IAsyncEnumerable<string> EnumerateByPrefixAsync(IEnumerable<string> keys, string prefix)
    {
        foreach (var key in keys.Where(k => k.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
            yield return key;

        yield break;
    }

    private static async IAsyncEnumerable<string> EmptyAsync()
    {
        yield break;
    }

    private static string NormalizeCollectionPrefix(PackageContentContext context)
    {
        var relativePath = context.Address!.RelativePath;
        if (!context.IsCollectionRequest)
            return relativePath;

        return relativePath.EndsWith("/", StringComparison.Ordinal) ? relativePath : $"{relativePath}/";
    }

    private static bool IsStatePath(string contentKind)
        => contentKind.StartsWith(".migration/", StringComparison.OrdinalIgnoreCase)
           || contentKind.Contains("/.migration/", StringComparison.OrdinalIgnoreCase);

    private static string ResolveMetaPath(PackageMetaContext context)
        => context.Kind switch
        {
            PackageMetaKind.ExecutionPlan => ".migration/plan.json",
            PackageMetaKind.PhaseRecord => ".migration/phase.json",
            PackageMetaKind.MigrationConfig => ".migration/migration-config.json",
            PackageMetaKind.CheckpointCursor => $".migration/{(context.Action ?? throw new InvalidOperationException("Action is required for checkpoint cursor metadata.")).ToLowerInvariant()}.{(context.Module ?? throw new InvalidOperationException("Module is required for checkpoint cursor metadata.")).ToLowerInvariant()}.cursor.json",
            PackageMetaKind.ContinuationToken => $".migration/{(context.Action ?? throw new InvalidOperationException("Action is required for continuation metadata.")).ToLowerInvariant()}.{(context.Module ?? throw new InvalidOperationException("Module is required for continuation metadata.")).ToLowerInvariant()}.continuation.json",
            PackageMetaKind.JobDescriptor => context.RelatedToRun
                ? throw new InvalidOperationException("Run-scoped job descriptor routing is not supported by test package mocks.")
                : ".migration/job.json",
            PackageMetaKind.RunConfigSnapshot => throw new InvalidOperationException("Run-scoped config routing is not supported by test package mocks."),
            PackageMetaKind.ExportProgressDb => ".migration/Checkpoints/export_progress.db",
            PackageMetaKind.IdMapDb => ".migration/Checkpoints/idmap.db",
            _ => throw new InvalidOperationException($"Unsupported meta kind for test package mock: {context.Kind}.")
        };

    private sealed class NoOpLockHandle : IDisposable
    {
        public void Dispose() { }
    }
}
