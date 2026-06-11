// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class InventoryModulesTests
{
    // --- Scenario: InventoryModules_AllModulesEnabled_ProducesPerModuleInventoryArtefacts ---

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("inventory")]
    [TestMethod]
    public async Task InventoryModules_AllModulesEnabled_ProducesPerModuleInventoryArtefacts()
    {
        var result = await InventoryModulesScenario
            .Arrange()
            .RunAsync();

        result.AssertAllStandardModuleArtefactsExist();
    }

    // --- Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts (analyser vocabulary) ---

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("inventory")]
    [TestCategory("multi-org")]
    [TestMethod]
    public async Task InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced()
    {
        var result = await InventoryModulesScenario
            .Arrange()
            .WithoutInventoryAnalyser()
            .RunAsync();

        // Guard: confirm the analyser was genuinely absent.
        Assert.IsFalse(result.InventoryAnalyserWasIncluded,
            "Test setup error: InventoryAnalyser should not have been included.");

        // Primary assertion: all four data-module artefacts are still present.
        result.AssertAllStandardModuleArtefactsExist();
    }

    // --- Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts ---

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("inventory")]
    [TestCategory("multi-org")]
    [TestMethod]
    public async Task InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced()
    {
        var result = await InventoryModulesScenario
            .Arrange()
            .WithoutInventoryDiscoveryModule()
            .RunAsync();

        // Guard: confirm the discovery module was genuinely absent.
        Assert.IsFalse(result.InventoryAnalyserWasIncluded,
            "Test setup error: InventoryDiscoveryModule (InventoryAnalyser) should not have been included.");

        // Primary assertion: all four data-module artefacts are still present.
        result.AssertAllStandardModuleArtefactsExist();
    }
}
