// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Creates <see cref="SqliteExportProgressStore"/> instances. Encapsulates path resolution
/// including legacy fallback so that module code does not call <c>File.Exists()</c> directly.
/// </summary>
public sealed class ExportProgressStoreFactory : IExportProgressStoreFactory
{
    /// <inheritdoc/>
    public IExportProgressStore Create(string dbFilePath)
        => new SqliteExportProgressStore(dbFilePath);

    /// <inheritdoc/>
    public IExportProgressStore CreateFromPackageUri(string packageUri)
    {
        string localRoot;
        if (packageUri.StartsWith("file:///", System.StringComparison.OrdinalIgnoreCase))
            localRoot = packageUri.Substring("file:///".Length).Replace('/', Path.DirectorySeparatorChar);
        else
            localRoot = packageUri;

        localRoot = Path.GetFullPath(localRoot);

        var newPath = PackagePaths.ExportProgressDbNative(localRoot);

        return new SqliteExportProgressStore(newPath);
    }
}
