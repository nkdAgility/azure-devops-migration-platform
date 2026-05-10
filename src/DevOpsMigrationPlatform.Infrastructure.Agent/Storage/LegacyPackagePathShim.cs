// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

[Obsolete(
    "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
    error: false)]
/// <summary>
/// Legacy compatibility shim for older path-based callers.
/// This is a transitional adapter over IPackageAccess, not the target package API.
/// New code must use IPackageAccess with typed contexts directly.
/// </summary>
public static class LegacyPackagePathShim
{
    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task<string?> ReadTextAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string path,
        CancellationToken ct)
    {
        _ = artefactStore;
        var resolvedPackage = Resolve(package);
        var payload = await resolvedPackage.RequestContentAsync(
            CreateContext(PackageContentKind.Artefact, path),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static Task<bool> ExistsAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string path,
        CancellationToken ct)
    {
        _ = artefactStore;
        return Resolve(package).ContentExistsAsync(
            CreateContext(PackageContentKind.Artefact, path),
            ct).AsTask();
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task WriteTextAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string path,
        string content,
        CancellationToken ct)
    {
        _ = artefactStore;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await Resolve(package).PersistContentAsync(
            CreateContext(PackageContentKind.Artefact, path),
            new PackagePayload(stream),
            ct).ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task AppendTextAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string path,
        string content,
        CancellationToken ct)
    {
        _ = artefactStore;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await Resolve(package).AppendContentAsync(
            CreateContext(PackageContentKind.Artefact, path),
            new PackagePayload(stream),
            ct).ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task WriteBinaryAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string path,
        byte[] content,
        CancellationToken ct)
    {
        _ = artefactStore;
        using var stream = new MemoryStream(content, writable: false);
        await Resolve(package).PersistContentStreamAsync(
            CreateContext(PackageContentKind.Artefact, path),
            stream,
            "application/octet-stream",
            ct).ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task<Stream?> ReadBinaryAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string path,
        CancellationToken ct)
    {
        _ = artefactStore;
        return await Resolve(package).RequestContentBinaryAsync(
            CreateContext(PackageContentKind.Artefact, path),
            ct).ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async IAsyncEnumerable<string> EnumerateAsync(
        IPackageAccess? package,
        IArtefactStore artefactStore,
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _ = artefactStore;
        var contentKind = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix;
        await foreach (var item in Resolve(package).EnumerateContentAsync(
            CreateContext(PackageContentKind.Collection, contentKind),
            ct).ConfigureAwait(false))
            yield return item;
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task<string?> ReadStateAsync(
        IPackageAccess? package,
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

        var payload = await resolvedPackage.RequestContentAsync(
            CreateContext(PackageContentKind.Artefact, key),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var stateReader = new StreamReader(payload.Content);
        return await stateReader.ReadToEndAsync().ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    public static async Task WriteStateAsync(
        IPackageAccess? package,
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
        await resolvedPackage.PersistContentAsync(
            CreateContext(PackageContentKind.Artefact, key),
            new PackagePayload(stream),
            ct).ConfigureAwait(false);
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    private static PackageContentContext CreateContext(PackageContentKind kind, string value)
    {
        return new PackageContentContext(kind, Address: new LegacyRelativePackageAddress(value));
    }

    [Obsolete(
        "Legacy compatibility shim for path-based package access. New code must use IPackageAccess directly via typed PackageContentContext/PackageMetaContext. Do not add new call sites.",
        error: false)]
    private static IPackageAccess Resolve(IPackageAccess? package)
        => package ?? throw new InvalidOperationException("IPackageAccess is required for runtime package access.");

    private sealed class LegacyRelativePackageAddress : IPackageContentAddress
    {
        public LegacyRelativePackageAddress(string value)
        {
            RelativePath = value.Replace('\\', '/').TrimStart('/');
        }

        public string RelativePath { get; }
    }
}
