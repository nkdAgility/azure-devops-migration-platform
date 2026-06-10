// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

/// <summary>
/// Production implementation of <see cref="IWorkItemFieldReader"/> that delegates
/// to a live <see cref="WorkItemStore"/> instance.
/// </summary>
internal sealed class WorkItemStoreFieldReader : IWorkItemFieldReader
{
    private readonly WorkItemStore _store;

    public WorkItemStoreFieldReader(WorkItemStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> GetFields(int id)
    {
        var wi = _store.GetWorkItem(id);
        var result = new Dictionary<string, object?>(wi.Fields.Count);
        foreach (Field f in wi.Fields)
        {
            result[f.ReferenceName] = f.Value;
        }
        return result;
    }
}
