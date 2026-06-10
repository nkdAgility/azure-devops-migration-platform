// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class InventoryModulesTests
{
    // --- Scenario 1 ---

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task InventoryModules_AllModulesEnabled_ProducesPerModuleInventoryArtefacts()
    {
        var result = await InventoryModulesScenario
            .Arrange()
            .RunAsync();

        result.AssertAllStandardModuleArtefactsExist();
    }

    // --- Scenario 2 ---

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestCategory("UnitTest")]
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
}
