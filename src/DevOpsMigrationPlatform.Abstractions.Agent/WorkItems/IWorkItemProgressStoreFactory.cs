// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Creates <see cref="IWorkItemProgressStore"/> instances for a given package.
/// </summary>
public interface IWorkItemProgressStoreFactory
{
    /// <summary>Creates a store backed by the file at <paramref name="dbFilePath"/>.</summary>
    IWorkItemProgressStore Create(string dbFilePath);

    /// <summary>Creates a store backed by an already resolved native database connection.</summary>
    IWorkItemProgressStore Create(System.Data.Common.DbConnection connection);

}
