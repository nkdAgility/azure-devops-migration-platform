// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.TfsMigrationAgent.Tests.WorkItems.Dsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests.WorkItems;

[TestClass]
public class TfsWorkItemFetchServiceTests
{
    // ── Scenario 1: Field Projection ──────────────────────────────────────────

    /// <summary>
    /// Only fields declared in <c>WorkItemFetchScope.Fields</c> appear in each
    /// yielded <c>FetchedWorkItem</c>; unrequested fields are never included.
    /// </summary>
    [TestMethod]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestCategory("tfs-object-model")]
    public async Task TfsWorkItemFetchService_FieldProjection_OnlyRequestedFieldsIncluded()
    {
        // Arrange — two work items each carrying three fields; scope requests only two.
        var harness = TfsWorkItemFetchHarness.Create()
            .WithWorkItem(TfsWorkItemBuilder.ForId(1)
                .WithType("Bug").WithState("Active").WithTitle("Bug one").Build())
            .WithWorkItem(TfsWorkItemBuilder.ForId(2)
                .WithType("Task").WithState("Closed").WithTitle("Task two").Build());

        var scope = TfsFetchScopeBuilder.WithFields("System.WorkItemType", "System.State");

        // Act
        var results = await harness.FetchAllAsync(scope);

        // Assert
        FetchedWorkItemCollectionAssertions.CountIs(results, 2);
        FetchedWorkItemCollectionAssertions.ContainsOnlyFields(
            results, "System.WorkItemType", "System.State");
    }

    // ── Scenario 2: In-Process Filter Exclusion ───────────────────────────────

    /// <summary>
    /// Items whose <c>System.WorkItemType</c> does not match the filter predicate
    /// are discarded before yield; only matching items are returned.
    /// </summary>
    [TestMethod]
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestCategory("tfs-object-model")]
    public async Task TfsWorkItemFetchService_FilterExclusion_OnlyMatchingTypeYielded()
    {
        // Arrange — three items of different types; filter requests only "Bug".
        var harness = TfsWorkItemFetchHarness.Create()
            .WithWorkItem(TfsWorkItemBuilder.ForId(1)
                .WithType("Bug").WithState("Active").Build())
            .WithWorkItem(TfsWorkItemBuilder.ForId(2)
                .WithType("Task").WithState("Active").Build())
            .WithWorkItem(TfsWorkItemBuilder.ForId(3)
                .WithType("Requirement").WithState("New").Build());

        var scope = TfsFetchScopeBuilder.WithFieldsAndTypeFilter(
            new[] { "System.WorkItemType", "System.State" },
            workItemType: "Bug");

        // Act
        var results = await harness.FetchAllAsync(scope);

        // Assert — only the Bug is yielded; Task and Requirement are discarded in-process.
        FetchedWorkItemCollectionAssertions.CountIs(results, 1);
        FetchedWorkItemCollectionAssertions.AllHaveType(results, "Bug");
        FetchedWorkItemCollectionAssertions.ContainsNoType(results, "Task");
        FetchedWorkItemCollectionAssertions.ContainsNoType(results, "Requirement");
    }
}
