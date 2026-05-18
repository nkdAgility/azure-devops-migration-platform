// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
    public IIdMapStore Create(System.Data.Common.DbConnection connection)
        => new SqliteIdMapStore(connection);

    /// <inheritdoc/>
    public IIdMapStore CreateFromPackageUri(string packageUri)
    {
        string localRoot;
        if (packageUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            localRoot = packageUri.Substring("file:///".Length).Replace('/', Path.DirectorySeparatorChar);
        else
            localRoot = packageUri;

        localRoot = Path.GetFullPath(localRoot);

        var newPath = Path.Combine(localRoot, ".migration", "Checkpoints", "idmap.db");

        return new SqliteIdMapStore(newPath);
    }
}
