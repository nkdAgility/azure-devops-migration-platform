// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

/// <summary>
/// Tests for the discover-work-items feature family:
/// project listing before counting begins, and UTC timestamp on progress updates.
/// </summary>
[TestClass]
public class InventoryProjectListingTests
{
    /// <summary>
    /// T-DWI-01 — All projects in the organisation are listed before counting begins.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_ListsAllProjects_BeforeCountingBegins()
    {
        // Arrange
        var harness = new InventoryProjectListingHarness()
            .WithOrganisationContaining("Alpha", "Beta", "Gamma");

        // Act — invoke the project-listing operation only (no counting triggered)
        var projectNames = await harness.ListProjectsAsync();

        // Assert — all three project names present
        ProjectListingAssertions.AssertContainsProjects(projectNames, "Alpha", "Beta", "Gamma");

        // Assert — no work item count data in the project list
        // (the listing result is IReadOnlyList<string>; absence of count fields is structural)
        ProjectListingAssertions.AssertNoWorkItemCountsInProjectList(projectNames);
    }

    /// <summary>
    /// T-DWI-02 — Each progress update includes the time it was recorded in UTC.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryService_ProgressUpdate_IncludesUtcTimestamp()
    {
        // Arrange — counting is in progress; the summary carries a UTC timestamp
        var utcNow = DateTime.UtcNow;
        var harness = new InventoryProjectListingHarness()
            .WithOrganisationContaining("Alpha")
            .WithCountingInProgress(lastUpdatedUtc: utcNow);

        // Act — run inventory; collect all emitted events
        var events = await harness.RunInventoryAsync();

        // Assert — every emitted event's Timestamp has DateTimeKind.Utc
        Assert.IsTrue(events.Count >= 1, "At least one progress event must be emitted");
        ProjectListingAssertions.AssertAllTimestampsAreUtc(events);
    }
}
