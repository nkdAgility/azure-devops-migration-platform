// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Creates <see cref="SqliteIdMapStore"/> instances. Encapsulates path resolution
/// including legacy fallback so that module code does not call <c>File.Exists()</c> directly.
/// </summary>
public sealed class IdMapStoreFactory : IIdMapStoreFactory
{
    /// <inheritdoc/>
    public IIdMapStore Create(string dbFilePath)
        => new SqliteIdMapStore(dbFilePath);

    /// <inheritdoc/>
    public IIdMapStore CreateFromPackageUri(string packageUri)
    {
        string localRoot;
        if (packageUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            localRoot = packageUri["file:///".Length..].Replace('/', Path.DirectorySeparatorChar);
        else
            localRoot = packageUri;

        localRoot = Path.GetFullPath(localRoot);

        var newPath = PackagePaths.IdMapDbNative(localRoot);

        return new SqliteIdMapStore(newPath);
    }
}
#endif
