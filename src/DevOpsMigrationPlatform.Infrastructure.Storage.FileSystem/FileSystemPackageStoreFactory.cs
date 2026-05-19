// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;

/// <summary>
/// <see cref="IPackageStoreFactory"/> implementation that resolves <c>file:///</c> URIs
/// to <see cref="FileSystemArtefactStore"/> and <see cref="FileSystemStateStore"/> pairs.
/// </summary>
internal sealed class FileSystemPackageStoreFactory : IPackageStoreFactory
{
    /// <inheritdoc/>
    public (IArtefactStore ArtefactStore, IStateStore StateStore) Create(string packageUri)
    {
        if (string.IsNullOrWhiteSpace(packageUri))
            throw new ArgumentException("packageUri must not be empty.", nameof(packageUri));

        var localPath = ResolveLocalPath(packageUri);
        return (new FileSystemArtefactStore(localPath), new FileSystemStateStore(localPath));
    }

    private static string ResolveLocalPath(string uri)
    {
        if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.Substring("file:///".Length).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(uri);
    }
}
