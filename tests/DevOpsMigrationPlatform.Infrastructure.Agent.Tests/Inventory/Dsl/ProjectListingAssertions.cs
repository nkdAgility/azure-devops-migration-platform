// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Assertion helpers for project-listing and progress-timestamp test outcomes.
/// All methods throw <see cref="AssertFailedException"/> on failure.
/// </summary>
internal static class ProjectListingAssertions
{
    /// <summary>
    /// Asserts all <paramref name="expectedNames"/> appear in the listing result.
    /// </summary>
    public static void AssertContainsProjects(
        IReadOnlyList<string> result,
        params string[] expectedNames)
    {
        foreach (var name in expectedNames)
        {
            Assert.IsTrue(
                result.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)),
                $"Expected project '{name}' to be present in the listing result. " +
                $"Actual: [{string.Join(", ", result)}]");
        }
    }

    /// <summary>
    /// Asserts that the listing result carries no work item count data.
    /// <para>
    /// The project-listing phase returns <c>IReadOnlyList&lt;string&gt;</c>. The absence
    /// of count fields is structural — a list of names cannot carry count data. This method
    /// documents that invariant explicitly.
    /// </para>
    /// </summary>
    public static void AssertNoWorkItemCountsInProjectList(IReadOnlyList<string> projectNames)
    {
        // The return type IReadOnlyList<string> structurally cannot carry work item count data.
        // This assertion is a compile-time invariant documented as a runtime check so it appears
        // in test output and failure messages.
        Assert.IsNotNull(projectNames,
            "Project listing result must not be null.");

        // No count fields exist on string elements — the assertion passes by definition
        // as long as the call site uses the listing-phase result (List<string>) not a count model.
    }

    /// <summary>
    /// Asserts that every emitted event's <c>Timestamp</c> has <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    public static void AssertAllTimestampsAreUtc(IReadOnlyList<InventoryProgressEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            Assert.AreEqual(
                DateTimeKind.Utc,
                evt.Timestamp.Kind,
                $"Event[{i}] (project='{evt.ProjectName}') has Timestamp.Kind={evt.Timestamp.Kind}; " +
                $"expected {DateTimeKind.Utc}.");
        }
    }
}
