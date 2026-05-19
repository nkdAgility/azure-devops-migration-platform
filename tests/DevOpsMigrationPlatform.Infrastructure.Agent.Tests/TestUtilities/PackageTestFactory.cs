// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

public interface ITestStateStore
{
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);
    Task WriteAsync(string key, string content, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface ITestArtefactStore
{
    Task<string?> ReadAsync(string path, CancellationToken cancellationToken);
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);
    Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken);
    Task<System.IO.Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken);
    IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken);
    Task WriteStreamAsync(string path, System.IO.Stream content, CancellationToken cancellationToken);
    Task AppendAsync(string path, string content, CancellationToken cancellationToken);
}

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
                var key = ResolveContentPath(context);
                if (!contentStore.TryGetValue(key, out var bytes))
                    return ValueTask.FromResult<PackagePayload?>(null);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false)));
            });
        package
            .Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var key = ResolveContentPath(context);
                if (!contentStore.TryGetValue(key, out var bytes))
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
            .Returns((PackageContentContext context, CancellationToken _) => ValueTask.FromResult(contentStore.ContainsKey(ResolveContentPath(context))));
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) => EnumerateByPrefixAsync(contentStore.Keys, NormalizeCollectionPrefix(context)));
        package
            .Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, PackagePayload payload, CancellationToken _) =>
            {
                contentStore[ResolveContentPath(context)] = ReadAllBytes(payload.Content);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, System.IO.Stream payload, string? _, CancellationToken _) =>
            {
                contentStore[ResolveContentPath(context)] = ReadAllBytes(payload);
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
            .Setup(p => p.ResetMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
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
                var key = ResolveContentPath(context);
                if (contentStore.TryGetValue(key, out var existing))
                    contentStore[key] = existing.Concat(incoming).ToArray();
                else
                    contentStore[key] = incoming;
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

    public static Mock<IPackageAccess> CreateDelegatingMock(FileSystemArtefactStore artefactStore)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var content = await artefactStore.ReadAsync(ResolveContentPath(context), ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct)
                => new ValueTask<bool>(artefactStore.ExistsAsync(ResolveContentPath(context), ct)));
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct)
                => artefactStore.EnumerateAsync(NormalizeCollectionPrefix(context), ct));
        package
            .Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var stream = await artefactStore.ReadBinaryAsync(ResolveContentPath(context), ct).ConfigureAwait(false);
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
                await artefactStore.WriteAsync(ResolveContentPath(context), text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, System.IO.Stream payload, string? _, CancellationToken ct) =>
            {
                var bytes = ReadAllBytes(payload);
                await artefactStore.WriteBinaryAsync(ResolveContentPath(context), bytes, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await artefactStore.AppendAsync(ResolveContentPath(context), text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) =>
            {
                var key = ResolveMetaPath(context);
                return ValueTask.FromResult(new PackageMetaResult(key, null));
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.ResetMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
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

    public static Mock<IPackageAccess> CreateDelegatingMock(ITestArtefactStore artefactStore)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var content = await artefactStore.ReadAsync(ResolveContentPath(context), ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct)
                => new ValueTask<bool>(artefactStore.ExistsAsync(ResolveContentPath(context), ct)));
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct)
                => artefactStore.EnumerateAsync(NormalizeCollectionPrefix(context), ct));
        package
            .Setup(p => p.RequestContentBinaryAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var stream = await artefactStore.ReadBinaryAsync(ResolveContentPath(context), ct).ConfigureAwait(false);
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
                await artefactStore.WriteAsync(ResolveContentPath(context), text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.PersistContentStreamAsync(It.IsAny<PackageContentContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, System.IO.Stream payload, string? _, CancellationToken ct) =>
            {
                var bytes = ReadAllBytes(payload);
                await artefactStore.WriteBinaryAsync(ResolveContentPath(context), bytes, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await artefactStore.AppendAsync(ResolveContentPath(context), text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) =>
            {
                var key = ResolveMetaPath(context);
                return ValueTask.FromResult(new PackageMetaResult(key, null));
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.ResetMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
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


    public static Mock<IPackageAccess> CreateCombinedDelegatingMock(ITestArtefactStore artefactStore, ITestStateStore stateStore)
    {
        var package = CreateDelegatingMock(artefactStore);
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
            .Setup(p => p.ResetMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken ct) => new ValueTask(stateStore.DeleteAsync(ResolveMetaPath(context), ct)));
        return package;
    }

    public static Mock<IPackageAccess> CreateStateDelegatingMock(ITestStateStore stateStore)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, CancellationToken ct) =>
            {
                var content = await stateStore.ReadAsync(ResolveContentPath(context), ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContentContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(ResolveContentPath(context), text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken ct) => new ValueTask<bool>(stateStore.ExistsAsync(ResolveContentPath(context), ct)));
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
            .Setup(p => p.ResetMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
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
                var key = ResolveContentPath(context);
                var existing = await stateStore.ReadAsync(key, ct).ConfigureAwait(false) ?? string.Empty;
                var incoming = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(key, existing + incoming, ct).ConfigureAwait(false);
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
        var relativePath = ResolveContentPath(context);
        if (!context.IsCollectionRequest)
            return relativePath;

        return relativePath.EndsWith("/", StringComparison.Ordinal) ? relativePath : $"{relativePath}/";
    }

    private static string ResolveContentPath(PackageContentContext context)
    {
        var relativePath = context.Address?.RelativePath ?? string.Empty;
        var module = context.Module ?? string.Empty;

        if (string.IsNullOrWhiteSpace(module))
            return relativePath;

        var normalizedModule = module.Trim('/').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relativePath))
            return normalizedModule;

        var normalizedPath = relativePath.Trim('/').Replace('\\', '/');
        if (normalizedPath.StartsWith($"{normalizedModule}/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedPath, normalizedModule, StringComparison.OrdinalIgnoreCase))
            return normalizedPath;

        return $"{normalizedModule}/{normalizedPath}";
    }

    private static string ResolveMetaPath(PackageMetaContext context)
        => context.Kind switch
        {
            PackageMetaKind.ExecutionPlan => ".migration/plan.json",
            PackageMetaKind.PhaseRecord => ".migration/Checkpoints/job.phase.json",
            PackageMetaKind.MigrationConfig => ".migration/migration-config.json",
            PackageMetaKind.InventoryCompletionMarker => ".migration/inventory.complete.json",
            PackageMetaKind.PrepareReport => ".migration/prepare-report.json",
            PackageMetaKind.PrepareProbe => ".migration/prepare-probe.json",
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
