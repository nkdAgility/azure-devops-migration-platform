// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Assertion helpers for inventory test outcomes.
/// All methods throw <see cref="AssertFailedException"/> on failure.
/// </summary>
internal static class InventoryResultAssertions
{
    /// <summary>
    /// Asserts the final complete event for <paramref name="project"/> has the given count.
    /// </summary>
    public static void AssertWorkItemCount(
        IReadOnlyList<InventoryProgressEvent> events, string project, int expected)
    {
        var final = events
            .Where(e => string.Equals(e.ProjectName, project, System.StringComparison.OrdinalIgnoreCase)
                        && e.IsComplete)
            .LastOrDefault();

        Assert.IsNotNull(final,
            $"No complete inventory event found for project '{project}'. " +
            $"Events: [{string.Join(", ", events.Select(e => $"{e.ProjectName}(complete={e.IsComplete})"))}]");

        Assert.AreEqual(expected, final.WorkItemsCount,
            $"Expected {expected} work items for project '{project}' but got {final.WorkItemsCount}.");
    }

    /// <summary>
    /// Asserts that the captured fetch scope's <c>BaseQuery</c> equals <paramref name="expectedQuery"/>.
    /// </summary>
    public static void AssertBaseQuery(WorkItemFetchScope? capturedScope, string expectedQuery)
    {
        Assert.IsNotNull(capturedScope,
            $"Expected a non-null WorkItemFetchScope with BaseQuery='{expectedQuery}' but scope was null.");
        Assert.AreEqual(expectedQuery, capturedScope.BaseQuery,
            $"Expected BaseQuery='{expectedQuery}' but got '{capturedScope.BaseQuery}'.");
    }

    /// <summary>
    /// Asserts that the captured fetch scope is null or its <c>BaseQuery</c> is null.
    /// Both represent "platform default in use": the default query is injected at the strategy
    /// layer, not stored in <c>WorkItemFetchScope.BaseQuery</c>.
    /// </summary>
    public static void AssertPlatformDefaultQuery(WorkItemFetchScope? capturedScope)
    {
        if (capturedScope is null)
            return; // null scope = no constraints = platform defaults throughout

        Assert.IsNull(capturedScope.BaseQuery,
            $"Expected null BaseQuery (platform default) but got '{capturedScope.BaseQuery}'.");
    }

}
