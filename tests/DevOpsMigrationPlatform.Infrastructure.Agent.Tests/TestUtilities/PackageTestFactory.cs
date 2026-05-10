// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

internal static class PackageTestFactory
{
    public static Mock<IPackage> CreateLooseMock()
    {
        var contentStore = new Dictionary<string, byte[]>(System.StringComparer.OrdinalIgnoreCase);
        var metaStore = new Dictionary<PackageMetaKind, byte[]>();
        var package = new Mock<IPackage>(MockBehavior.Loose);
        package
            .Setup(p => p.RequestAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken _) =>
            {
                if (!contentStore.TryGetValue(context.ContentKind, out var bytes))
                    return ValueTask.FromResult<PackagePayload?>(null);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false)));
            });
        package
            .Setup(p => p.RequestBinaryAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken _) =>
            {
                if (!contentStore.TryGetValue(context.ContentKind, out var bytes))
                    return ValueTask.FromResult<PackagePayload?>(null);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false)));
            });
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, CancellationToken _) =>
            {
                if (!metaStore.TryGetValue(context.Kind, out var bytes))
                    return ValueTask.FromResult<PackageMetaPayload?>(null);
                return ValueTask.FromResult<PackageMetaPayload?>(new PackageMetaPayload(new System.IO.MemoryStream(bytes, writable: false)));
            });
        package
            .Setup(p => p.ExistsAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken _) => ValueTask.FromResult(contentStore.ContainsKey(context.ContentKind)));
        package
            .Setup(p => p.EnumerateAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken _) => EnumerateByPrefixAsync(contentStore.Keys, context.ContentKind));
        package
            .Setup(p => p.PersistAsync(It.IsAny<PackageContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, PackagePayload payload, CancellationToken _) =>
            {
                contentStore[context.ContentKind] = ReadAllBytes(payload.Content);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.PersistStreamAsync(It.IsAny<PackageContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, System.IO.Stream payload, string? _, CancellationToken _) =>
            {
                contentStore[context.ContentKind] = ReadAllBytes(payload);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext context, PackageMetaPayload payload, CancellationToken _) =>
            {
                metaStore[context.Kind] = ReadAllBytes(payload.Content);
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.AppendAsync(It.IsAny<PackageContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, PackagePayload payload, CancellationToken _) =>
            {
                var incoming = ReadAllBytes(payload.Content);
                if (contentStore.TryGetValue(context.ContentKind, out var existing))
                    contentStore[context.ContentKind] = existing.Concat(incoming).ToArray();
                else
                    contentStore[context.ContentKind] = incoming;
                return ValueTask.CompletedTask;
            });
        package
            .Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        return package;
    }

    public static Mock<IPackage> CreateDelegatingMock(IArtefactStore artefactStore, IStateStore? stateStore = null)
    {
        var package = new Mock<IPackage>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, CancellationToken ct) =>
            {
                var content = IsStatePath(context.ContentKind) && stateStore is not null
                    ? await stateStore.ReadAsync(context.ContentKind, ct).ConfigureAwait(false)
                    : await artefactStore.ReadAsync(context.ContentKind, ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.ExistsAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken ct)
                => IsStatePath(context.ContentKind) && stateStore is not null
                    ? new ValueTask<bool>(stateStore.ExistsAsync(context.ContentKind, ct))
                    : new ValueTask<bool>(artefactStore.ExistsAsync(context.ContentKind, ct)));
        package
            .Setup(p => p.EnumerateAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken ct) => artefactStore.EnumerateAsync(context.ContentKind, ct));
        package
            .Setup(p => p.RequestBinaryAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, CancellationToken ct) =>
            {
                var stream = await artefactStore.ReadBinaryAsync(context.ContentKind, ct).ConfigureAwait(false);
                if (stream is null)
                    return null;
                if (stream.CanSeek)
                    stream.Position = 0;
                return new PackagePayload(stream, "application/octet-stream");
            });
        package
            .Setup(p => p.PersistAsync(It.IsAny<PackageContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                if (IsStatePath(context.ContentKind) && stateStore is not null)
                    await stateStore.WriteAsync(context.ContentKind, text, ct).ConfigureAwait(false);
                else
                    await artefactStore.WriteAsync(context.ContentKind, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.PersistStreamAsync(It.IsAny<PackageContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, System.IO.Stream payload, string? _, CancellationToken ct) =>
            {
                var bytes = ReadAllBytes(payload);
                await artefactStore.WriteBinaryAsync(context.ContentKind, bytes, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendAsync(It.IsAny<PackageContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                if (IsStatePath(context.ContentKind) && stateStore is not null)
                {
                    var existing = await stateStore.ReadAsync(context.ContentKind, ct).ConfigureAwait(false) ?? string.Empty;
                    await stateStore.WriteAsync(context.ContentKind, existing + text, ct).ConfigureAwait(false);
                }
                else
                {
                    await artefactStore.AppendAsync(context.ContentKind, text, ct).ConfigureAwait(false);
                }
            });
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, CancellationToken ct) =>
            {
                if (stateStore is null)
                    return null;

                var key = context.Kind switch
                {
                    PackageMetaKind.ExecutionPlan => PackagePaths.PlanFile,
                    PackageMetaKind.PhaseRecord => PackagePaths.PhaseFile,
                    _ => null
                };
                if (key is null)
                    return null;
                var content = await stateStore.ReadAsync(key, ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackageMetaPayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, PackageMetaPayload payload, CancellationToken ct) =>
            {
                if (stateStore is null)
                    return;
                var key = context.Kind switch
                {
                    PackageMetaKind.ExecutionPlan => PackagePaths.PlanFile,
                    PackageMetaKind.PhaseRecord => PackagePaths.PhaseFile,
                    _ => null
                };
                if (key is null)
                    return;
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(key, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        return package;
    }

    public static Mock<IPackage> CreateStateDelegatingMock(IStateStore stateStore)
    {
        var package = new Mock<IPackage>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, CancellationToken ct) =>
            {
                var content = await stateStore.ReadAsync(context.ContentKind, ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackagePayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.PersistAsync(It.IsAny<PackageContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(context.ContentKind, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.ExistsAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContext context, CancellationToken ct) => new ValueTask<bool>(stateStore.ExistsAsync(context.ContentKind, ct)));
        package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, CancellationToken ct) =>
            {
                var key = context.Kind switch
                {
                    PackageMetaKind.ExecutionPlan => PackagePaths.PlanFile,
                    PackageMetaKind.PhaseRecord => PackagePaths.PhaseFile,
                    _ => null
                };
                if (key is null)
                    return null;
                var content = await stateStore.ReadAsync(key, ct).ConfigureAwait(false);
                if (content is null)
                    return null;
                return new PackageMetaPayload(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content), writable: false), "application/json");
            });
        package
            .Setup(p => p.PersistMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<PackageMetaPayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageMetaContext context, PackageMetaPayload payload, CancellationToken ct) =>
            {
                var key = context.Kind switch
                {
                    PackageMetaKind.ExecutionPlan => PackagePaths.PlanFile,
                    PackageMetaKind.PhaseRecord => PackagePaths.PhaseFile,
                    _ => null
                };
                if (key is null)
                    return;
                var text = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(key, text, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.EnumerateAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());
        package
            .Setup(p => p.RequestBinaryAsync(It.IsAny<PackageContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));
        package
            .Setup(p => p.PersistStreamAsync(It.IsAny<PackageContext>(), It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        package
            .Setup(p => p.AppendAsync(It.IsAny<PackageContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns(async (PackageContext context, PackagePayload payload, CancellationToken ct) =>
            {
                var existing = await stateStore.ReadAsync(context.ContentKind, ct).ConfigureAwait(false) ?? string.Empty;
                var incoming = Encoding.UTF8.GetString(ReadAllBytes(payload.Content));
                await stateStore.WriteAsync(context.ContentKind, existing + incoming, ct).ConfigureAwait(false);
            });
        package
            .Setup(p => p.AppendLogAsync(It.IsAny<PackageLogContext>(), It.IsAny<PackageLogPayload>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
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

    private static bool IsStatePath(string contentKind)
        => contentKind.StartsWith(".migration/", StringComparison.OrdinalIgnoreCase)
           || contentKind.Contains("/.migration/", StringComparison.OrdinalIgnoreCase);
}
