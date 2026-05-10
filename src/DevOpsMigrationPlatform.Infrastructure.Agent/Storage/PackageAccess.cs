// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

public static class PackageAccess
{
    public static async Task<string?> ReadTextAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string path,
        CancellationToken ct)
    {
        _ = artefactStore;
        var resolvedPackage = Resolve(package);
        var payload = await resolvedPackage.RequestAsync(new PackageContext(path), ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public static Task<bool> ExistsAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string path,
        CancellationToken ct)
    {
        _ = artefactStore;
        return Resolve(package).ExistsAsync(new PackageContext(path), ct).AsTask();
    }

    public static async Task WriteTextAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string path,
        string content,
        CancellationToken ct)
    {
        _ = artefactStore;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await Resolve(package).PersistAsync(new PackageContext(path), new PackagePayload(stream), ct).ConfigureAwait(false);
    }

    public static async Task AppendTextAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string path,
        string content,
        CancellationToken ct)
    {
        _ = artefactStore;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await Resolve(package).AppendAsync(new PackageContext(path), new PackagePayload(stream), ct).ConfigureAwait(false);
    }

    public static async Task WriteBinaryAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string path,
        byte[] content,
        CancellationToken ct)
    {
        _ = artefactStore;
        using var stream = new MemoryStream(content, writable: false);
        await Resolve(package).PersistStreamAsync(new PackageContext(path), stream, "application/octet-stream", ct).ConfigureAwait(false);
    }

    public static async Task<Stream?> ReadBinaryAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string path,
        CancellationToken ct)
    {
        _ = artefactStore;
        var payload = await Resolve(package).RequestBinaryAsync(new PackageContext(path), ct).ConfigureAwait(false);
        return payload?.Content;
    }

    public static async IAsyncEnumerable<string> EnumerateAsync(
        IPackage? package,
        IArtefactStore artefactStore,
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _ = artefactStore;
        await foreach (var item in Resolve(package).EnumerateAsync(new PackageContext(prefix), ct).ConfigureAwait(false))
            yield return item;
    }

    public static async Task<string?> ReadStateAsync(
        IPackage? package,
        IStateStore stateStore,
        string key,
        CancellationToken ct)
    {
        _ = stateStore;
        var resolvedPackage = Resolve(package);
        if (string.Equals(key, PackagePaths.PlanFile, StringComparison.Ordinal))
        {
            var planMeta = await resolvedPackage.RequestMetaAsync(
                new PackageMetaContext(PackageMetaKind.ExecutionPlan), ct).ConfigureAwait(false);
            if (planMeta is null)
                return null;

            if (planMeta.Content.CanSeek)
                planMeta.Content.Position = 0;
            using var reader = new StreamReader(planMeta.Content);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        if (string.Equals(key, PackagePaths.PhaseFile, StringComparison.Ordinal))
        {
            var phaseMeta = await resolvedPackage.RequestMetaAsync(
                new PackageMetaContext(PackageMetaKind.PhaseRecord), ct).ConfigureAwait(false);
            if (phaseMeta is null)
                return null;

            if (phaseMeta.Content.CanSeek)
                phaseMeta.Content.Position = 0;
            using var reader = new StreamReader(phaseMeta.Content);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var payload = await resolvedPackage.RequestAsync(new PackageContext(key), ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var stateReader = new StreamReader(payload.Content);
        return await stateReader.ReadToEndAsync().ConfigureAwait(false);
    }

    public static async Task WriteStateAsync(
        IPackage? package,
        IStateStore stateStore,
        string key,
        string value,
        CancellationToken ct)
    {
        _ = stateStore;
        var resolvedPackage = Resolve(package);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(value), writable: false);
        if (string.Equals(key, PackagePaths.PlanFile, StringComparison.Ordinal))
        {
            await resolvedPackage.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.ExecutionPlan),
                new PackageMetaPayload(stream),
                ct).ConfigureAwait(false);
            return;
        }
        if (string.Equals(key, PackagePaths.PhaseFile, StringComparison.Ordinal))
        {
            await resolvedPackage.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.PhaseRecord),
                new PackageMetaPayload(stream),
                ct).ConfigureAwait(false);
            return;
        }
        await resolvedPackage.PersistAsync(new PackageContext(key), new PackagePayload(stream), ct).ConfigureAwait(false);
    }

    private static IPackage Resolve(IPackage? package)
        => package ?? throw new InvalidOperationException("IPackage is required for runtime package access.");
}
