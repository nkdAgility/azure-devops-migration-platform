// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests.WorkItems.Dsl;

/// <summary>
/// MSTest assertion helpers for collections of <see cref="FetchedWorkItem"/>.
/// All methods throw <see cref="AssertFailedException"/> on failure.
/// </summary>
internal static class FetchedWorkItemCollectionAssertions
{
    /// <summary>
    /// Asserts every item in <paramref name="items"/> contains exactly the field keys in
    /// <paramref name="expectedFields"/> — no more, no fewer — for fields that exist on the item.
    /// Unrequested fields must be absent.
    /// </summary>
    public static void ContainsOnlyFields(
        IReadOnlyList<FetchedWorkItem> items,
        params string[] expectedFields)
    {
        foreach (var item in items)
        {
            var unexpected = item.Fields.Keys
                .Except(expectedFields, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.AreEqual(0, unexpected.Count,
                $"Item {item.Id} contains unrequested fields: [{string.Join(", ", unexpected)}].");
        }
    }

    /// <summary>
    /// Asserts that all yielded items carry <paramref name="expectedType"/> in
    /// <c>System.WorkItemType</c>.
    /// </summary>
    public static void AllHaveType(IReadOnlyList<FetchedWorkItem> items, string expectedType)
    {
        foreach (var item in items)
        {
            item.Fields.TryGetValue("System.WorkItemType", out var actual);
            Assert.IsTrue(
                string.Equals(actual?.ToString(), expectedType, System.StringComparison.OrdinalIgnoreCase),
                $"Item {item.Id} has type '{actual}'; expected '{expectedType}'.");
        }
    }

    /// <summary>
    /// Asserts the exact count of yielded items.
    /// </summary>
    public static void CountIs(IReadOnlyList<FetchedWorkItem> items, int expected) =>
        Assert.AreEqual(expected, items.Count,
            $"Expected {expected} yielded items but got {items.Count}.");

    /// <summary>
    /// Asserts that no yielded item carries <paramref name="excludedType"/> in
    /// <c>System.WorkItemType</c>.
    /// </summary>
    public static void ContainsNoType(IReadOnlyList<FetchedWorkItem> items, string excludedType)
    {
        var present = items
            .Where(i => {
                i.Fields.TryGetValue("System.WorkItemType", out var v);
                return string.Equals(v?.ToString(), excludedType, System.StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        Assert.AreEqual(0, present.Count,
            $"Expected no items of type '{excludedType}' but found {present.Count}.");
    }
}
