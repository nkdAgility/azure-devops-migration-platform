// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Identity;

/// <summary>
/// Creates <see cref="SqliteIdMapStore"/> instances.
/// </summary>
public sealed class IdMapStoreFactory : IIdMapStoreFactory
{
    /// <inheritdoc/>
    public IIdMapStore Create(string dbFilePath)
        => new SqliteIdMapStore(dbFilePath);

    /// <inheritdoc/>
    public IIdMapStore Create(System.Data.Common.DbConnection connection)
        => new SqliteIdMapStore(connection);
}
