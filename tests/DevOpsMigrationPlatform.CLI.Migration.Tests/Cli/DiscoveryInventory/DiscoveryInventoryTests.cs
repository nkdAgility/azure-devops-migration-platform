// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryInventory;

/// <summary>
/// Code-first MSTest tests for the Discovery Inventory CLI command.
/// Migrated from feature file: features/cli/inventory/workitem-inventory.feature
/// Feature family: workitem-inventory
/// </summary>
[TestClass]
public sealed class DiscoveryInventoryTests
{
    // ── Capability: Live Table Rendering ──────────────────────────────────────

    /// <summary>S1 — Live table appears immediately and updates as each project is counted.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DiscoveryInventory_LiveTable_AppearsImmediatelyAndUpdatesPerProject()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithProjects("Alpha", "Beta", "Gamma")
            .RunInProcessAsync();

        result
            .AssertTableRendered()
            .AssertProjectRowShowsFinalCount("Alpha", expectedWorkItemCount: 5)
            .AssertProjectRowShowsFinalCount("Beta", expectedWorkItemCount: 5)
            .AssertProjectRowShowsFinalCount("Gamma", expectedWorkItemCount: 5);
    }

    /// <summary>S2 — Table shows all expected columns.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task DiscoveryInventory_Table_ShowsAllExpectedColumns()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithProjects("Alpha")
            .RunInProcessAsync();

        result
            .AssertTableRendered()
            .AssertTableHasColumns("Project", "Work Items", "Revisions", "Repos", "Pipelines", "Updated");
    }

    /// <summary>S3 — Updated column reflects the time of the last count update for each project.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task DiscoveryInventory_UpdatedColumn_ReflectsLastCountUpdateTime()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithProjects("Alpha")
            .RunInProcessAsync();

        result.AssertUpdatedCellFormat("Alpha");
    }

    // ── Capability: Output File Production ───────────────────────────────────

    /// <summary>S4 — On completion a CSV summary file is saved to the working directory.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DiscoveryInventory_Completion_CsvSavedToOutputDirectory()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithProjects("Alpha", "Beta", "Gamma")
            .RunInProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertCsvCreated()
            .AssertCsvRowCount(3)
            .AssertTerminalConfirmsFilePath();
    }

    // ── Capability: Empty Organisation Handling ───────────────────────────────

    /// <summary>S5 — An organisation with zero projects completes without error.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DiscoveryInventory_ZeroProjects_CompletesCleanlyWithEmptyCsv()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithZeroProjects()
            .RunInProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertTableRendered()
            .AssertCsvCreated()
            .AssertCsvHeaderOnly();
    }

    // ── Capability: Authentication Failure Handling ───────────────────────────

    /// <summary>S6 — An invalid PAT causes the command to exit with a non-zero code.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DiscoveryInventory_InvalidPat_ExitsWithNonZeroCodeAndErrorMessage()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithInvalidPat()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeNonZero()
            .AssertAuthenticationFailureMessage();
    }

    // ── Capability: Sequential Project Counting ───────────────────────────────

    /// <summary>S7 — Projects are counted sequentially and the table reflects each update in real time.</summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DiscoveryInventory_Sequential_ProjectCountingOrder()
    {
        await using var result = await DiscoveryInventoryScenario
            .Arrange()
            .WithSequentialProjects("P1", "P2", "P3", "P4", "P5")
            .RunInProcessAsync();

        result
            .AssertProjectCountedBefore("P1", "P2")
            .AssertProjectCountedBefore("P2", "P3")
            .AssertProjectCountedBefore("P3", "P4")
            .AssertProjectCountedBefore("P4", "P5");
    }
}
