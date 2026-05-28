// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Creates <see cref="SqliteExportProgressStore"/> instances.
/// </summary>
public sealed class ExportProgressStoreFactory : IExportProgressStoreFactory
{
    /// <inheritdoc/>
    public IExportProgressStore Create(string dbFilePath)
        => new SqliteExportProgressStore(dbFilePath);

    /// <inheritdoc/>
    public IExportProgressStore Create(System.Data.Common.DbConnection connection)
        => new SqliteExportProgressStore(connection);
}
