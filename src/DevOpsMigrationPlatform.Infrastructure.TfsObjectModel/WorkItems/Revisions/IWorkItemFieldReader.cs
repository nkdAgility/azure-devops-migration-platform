// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

/// <summary>
/// Abstracts field-value lookup over a TFS <see cref="Microsoft.TeamFoundation.WorkItemTracking.Client.WorkItemStore"/>
/// so that <see cref="TfsWorkItemFetchService"/> can be tested without a real TFS connection.
/// Production implementation delegates to <c>WorkItemStore.GetWorkItem(id).Fields</c>.
/// </summary>
internal interface IWorkItemFieldReader
{
    /// <summary>Returns the available field values for the given work-item ID.</summary>
    IReadOnlyDictionary<string, object?> GetFields(int id);
}
