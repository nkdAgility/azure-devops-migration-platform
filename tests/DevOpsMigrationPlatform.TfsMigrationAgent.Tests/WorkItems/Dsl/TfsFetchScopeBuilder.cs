// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests.WorkItems.Dsl;

/// <summary>
/// Builds <see cref="WorkItemFetchScope"/> values for TFS field-projection and filter tests.
/// </summary>
internal static class TfsFetchScopeBuilder
{
    /// <summary>Scope requesting exactly the specified fields.</summary>
    public static WorkItemFetchScope WithFields(params string[] fields) =>
        new WorkItemFetchScope(Fields: fields);

    /// <summary>Scope requesting <paramref name="fields"/> and a single type-equality filter.</summary>
    public static WorkItemFetchScope WithFieldsAndTypeFilter(string[] fields, string workItemType) =>
        new WorkItemFetchScope(
            Fields: fields,
            FilterOptions: new[]
            {
                new WorkItemFieldFilterOptions("System.WorkItemType", FilterOperator.Equals, workItemType)
            });
}
