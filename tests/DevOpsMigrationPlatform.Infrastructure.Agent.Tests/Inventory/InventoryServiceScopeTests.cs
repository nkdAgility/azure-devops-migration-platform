// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

/// <summary>
/// MSTest suite for <see cref="DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.InventoryService"/>
/// scoping behaviour (WIQL scope and filter scope).
///
/// Scenarios S1–S6 replace Reqnroll scenarios from
/// <c>features/inventory/work-items/filter-scope-inventory.feature</c>.
/// Scenario S7 is covered by the pre-existing test
/// <c>DiscoverWorkItemsAsync_WithFilterScope_UnionsFieldsWithSystemRev</c>
/// in <see cref="InventoryServiceTests"/>.
/// </summary>
[TestClass]
public class InventoryServiceScopeTests
{
    // ── S1: WiqlScope flows from config to discovery ───────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_WiqlScope_UsesCustomQueryForDiscovery()
    {
        // Arrange
        const string customQuery =
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project " +
            "AND [System.State] = 'Active' ORDER BY [System.Id]";

        var harness = InventoryServiceHarness.Create()
            .WithOrganisation(OrganisationEntryBuilder.WithWiqlScope(customQuery));

        // Act
        await harness.RunAsync();

        // Assert
        InventoryResultAssertions.AssertBaseQuery(harness.CapturedFetchScope, customQuery);
    }

    // ── S2: No WiqlScope uses platform default ─────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_NoWiqlScope_UsesPlatformDefaultQuery()
    {
        // Arrange
        var harness = InventoryServiceHarness.Create()
            .WithOrganisation(OrganisationEntryBuilder.Default());

        // Act
        await harness.RunAsync();

        // Assert
        InventoryResultAssertions.AssertPlatformDefaultQuery(harness.CapturedFetchScope);
    }

    // ── S3: Empty WiqlScope.Query falls back to platform default ──────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_EmptyWiqlQuery_FallsBackToPlatformDefault()
    {
        // Arrange
        var harness = InventoryServiceHarness.Create()
            .WithOrganisation(OrganisationEntryBuilder.WithEmptyWiqlScope());

        // Act
        await harness.RunAsync();

        // Assert: empty string must NOT be forwarded; result is indistinguishable from no-scope
        InventoryResultAssertions.AssertPlatformDefaultQuery(harness.CapturedFetchScope);
    }

    // ── S4: FilterScope reduces inventory count to matching items only ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_FilterScope_CountsOnlyMatchingWorkItems()
    {
        // Arrange: 4 items — 2 under TeamA (match), 2 under Archived (no match)
        var feed = WorkItemFeedBuilder.Mixed(
            (@"MyOrg\TeamA",    2),
            (@"MyOrg\Archived", 2));

        var harness = InventoryServiceHarness.Create()
            .WithRealDiscovery()
            .WithOrganisation(OrganisationEntryBuilder.WithFilterScope(
                field: "System.AreaPath",
                pattern: @"^MyOrg\\TeamA",
                mode: "include"));
        harness.WorkItemFeed = feed;

        // Act
        var results = await harness.RunAsync();

        // Assert
        InventoryResultAssertions.AssertWorkItemCount(results, "TestProject", expected: 2);
    }

    // ── S5: Combined WiqlScope and FilterScope apply both constraints ──────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_CombinedWiqlAndFilterScope_AppliesBothConstraints()
    {
        // Arrange: 5 items — 2 active/TeamA, 1 active/Archived, 2 closed/Archived
        // The WIQL scope carries the custom base query (forwarded to discovery).
        // The filter scope post-filters to TeamA area path.
        // The mock fetch service applies FilterOptions only (not WIQL), so the 2 active+TeamA
        // items and 0 closed items under TeamA = 2 items match.
        // AssertBaseQuery verifies the WIQL scope was wired into BaseQuery.
        // AssertWorkItemCount verifies the filter scope reduced the count to 2.
        const string activeQuery =
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project " +
            "AND [System.State] = 'Active' ORDER BY [System.Id]";

        var feed = WorkItemFeedBuilder.WithStateAndAreaPath(
            ("Active", @"MyOrg\TeamA",    2),
            ("Active", @"MyOrg\Archived", 1),
            ("Closed", @"MyOrg\Archived", 2));

        var harness = InventoryServiceHarness.Create()
            .WithRealDiscovery()
            .WithOrganisation(OrganisationEntryBuilder.WithWiqlAndFilterScope(
                query:   activeQuery,
                field:   "System.AreaPath",
                pattern: @"^MyOrg\\TeamA",
                mode:    "include"));
        harness.WorkItemFeed = feed;

        // Act
        var results = await harness.RunAsync();

        // Assert: WIQL scope captured, filter applied
        InventoryResultAssertions.AssertBaseQuery(harness.CapturedFetchScope, activeQuery);
        InventoryResultAssertions.AssertWorkItemCount(results, "TestProject", expected: 2);
    }

    // ── S5b: TypeFilterScope counts only the matching work-item type ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_TypeFilterScope_CountsOnlyMatchingWorkItemType()
    {
        // Arrange: mixed feed — 3 Bug, 2 Task, 1 Epic = 6 total; scope restricts to Bug
        // WorkItemFieldFilterEvaluator evaluates System.WorkItemType against the ^Bug$ regex.
        // Task and Epic items are discarded inside FilteredFeedStream — they are never
        // yielded to AzureDevOpsWorkItemDiscoveryService and therefore cannot reach any store.
        var feed = WorkItemFeedBuilder.MixedTypes(
            ("Bug",  3),
            ("Task", 2),
            ("Epic", 1));

        var harness = InventoryServiceHarness.Create()
            .WithRealDiscovery()
            .WithOrganisation(OrganisationEntryBuilder.WithFilterScope(
                field:   "System.WorkItemType",
                pattern: "^Bug$",
                mode:    "include"));
        harness.WorkItemFeed = feed;

        // Act
        var results = await harness.RunAsync();

        // Assert: only the 3 Bug items are counted; Task and Epic are silently discarded
        InventoryResultAssertions.AssertWorkItemCount(results, "TestProject", expected: 3);
    }

    // ── S6: Org-A scopes do not contaminate Org-B discovery ───────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_MultiOrg_UnScopedOrgUsesPlatformDefault()
    {
        // Arrange: org-a has a custom WiqlScope; org-b has none
        const string orgAQuery =
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project " +
            "AND [System.State] = 'Active' ORDER BY [System.Id]";

        var harness = InventoryServiceHarness.Create()
            .WithOrganisations(
                OrganisationEntryBuilder.WithWiqlScope(orgAQuery, orgKey: "org-a"),
                OrganisationEntryBuilder.Default(orgKey: "org-b"));

        // Act
        await harness.RunAsync();

        // Assert
        var orgAScope = harness.CapturedFetchScopesPerOrg[0];
        var orgBScope = harness.CapturedFetchScopesPerOrg[1];

        InventoryResultAssertions.AssertBaseQuery(orgAScope, orgAQuery);
        InventoryResultAssertions.AssertPlatformDefaultQuery(orgBScope);
    }
}
