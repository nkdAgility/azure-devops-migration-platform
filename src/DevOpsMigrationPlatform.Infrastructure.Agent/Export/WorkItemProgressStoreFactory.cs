// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Creates <see cref="SqliteWorkItemProgressStore"/> instances.
/// </summary>
public sealed class WorkItemProgressStoreFactory : IWorkItemProgressStoreFactory
{
    /// <inheritdoc/>
    public IWorkItemProgressStore Create(string dbFilePath)
        => new SqliteWorkItemProgressStore(dbFilePath);

    /// <inheritdoc/>
    public IWorkItemProgressStore Create(System.Data.Common.DbConnection connection)
        => new SqliteWorkItemProgressStore(connection);
}
