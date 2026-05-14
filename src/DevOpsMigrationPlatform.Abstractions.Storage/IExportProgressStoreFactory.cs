// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Creates <see cref="IExportProgressStore"/> instances for a given package.
/// </summary>
public interface IExportProgressStoreFactory
{
    /// <summary>Creates a store backed by the file at <paramref name="dbFilePath"/>.</summary>
    IExportProgressStore Create(string dbFilePath);

    /// <summary>Creates a store backed by an already resolved native database connection.</summary>
    IExportProgressStore Create(System.Data.Common.DbConnection connection);

    /// <summary>
    /// Resolves the DB path from <paramref name="packageUri"/> (a <c>file:///</c> URI or a local path),
    /// applying the same legacy-fallback logic as <see cref="IIdMapStoreFactory"/>.
    /// </summary>
    [System.Obsolete("Use IPackageAccess.OpenNativeDatabaseAsync(...) with Create(DbConnection). Removed in v5.")]
    IExportProgressStore CreateFromPackageUri(string packageUri);
}
