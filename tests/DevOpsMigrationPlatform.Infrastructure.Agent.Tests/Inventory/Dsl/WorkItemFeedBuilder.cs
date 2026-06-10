// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Builds simple <see cref="FetchedWorkItem"/> lists for filter-scope tests.
/// Each item records the fields the filter evaluates against.
/// </summary>
internal static class WorkItemFeedBuilder
{
    private static int _nextId;

    /// <summary>
    /// Returns a feed of <paramref name="count"/> items all carrying the given <paramref name="areaPath"/>
    /// in their field dictionary under <c>System.AreaPath</c>.
    /// </summary>
    public static IReadOnlyList<FetchedWorkItem> WithAreaPath(string areaPath, int count)
    {
        var items = new List<FetchedWorkItem>(count);
        for (var i = 0; i < count; i++)
            items.Add(MakeItem(areaPath: areaPath));
        return items.AsReadOnly();
    }

    /// <summary>
    /// Returns a mixed feed. Each tuple is (areaPath, count).
    /// </summary>
    public static IReadOnlyList<FetchedWorkItem> Mixed(params (string areaPath, int count)[] groups)
    {
        var items = new List<FetchedWorkItem>();
        foreach (var (areaPath, count) in groups)
            for (var i = 0; i < count; i++)
                items.Add(MakeItem(areaPath: areaPath));
        return items.AsReadOnly();
    }

    /// <summary>
    /// Returns a feed mixing state and area path.
    /// Each tuple is (state, areaPath, count).
    /// </summary>
    public static IReadOnlyList<FetchedWorkItem> WithStateAndAreaPath(
        params (string state, string areaPath, int count)[] groups)
    {
        var items = new List<FetchedWorkItem>();
        foreach (var (state, areaPath, count) in groups)
            for (var i = 0; i < count; i++)
                items.Add(MakeItem(areaPath: areaPath, state: state));
        return items.AsReadOnly();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static FetchedWorkItem MakeItem(string? areaPath = null, string? state = null)
    {
        var fields = new Dictionary<string, object?>();
        if (areaPath is not null)
            fields["System.AreaPath"] = areaPath;
        if (state is not null)
            fields["System.State"] = state;
        // System.Rev = 1 is required by AzureDevOpsWorkItemDiscoveryService for revision counting
        fields["System.Rev"] = 1;

        return new FetchedWorkItem(++_nextId, fields);
    }
}
