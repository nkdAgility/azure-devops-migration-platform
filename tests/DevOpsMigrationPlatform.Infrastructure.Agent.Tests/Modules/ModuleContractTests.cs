// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public class ModuleContractTests
{
    [TestMethod]
    [TestCategory("L0")]
    public void ModuleContract_ExposesThreeAspectCollections()
    {
        var contract = new ModuleContract(
            moduleName: "WorkItems",
            selection: [new SelectionDefinition("Query", Required: true), new SelectionDefinition("Filters", Required: false)],
            data: [new DataDefinition("Revisions", Required: false)],
            processing: [new ProcessingDefinition("WorkItemResolutionStrategy", Required: false)]);

        Assert.AreEqual("WorkItems", contract.ModuleName);
        Assert.AreEqual(2, contract.Selection.Count);
        Assert.AreEqual(1, contract.Data.Count);
        Assert.AreEqual(1, contract.Processing.Count);
        Assert.IsTrue(contract.Selection[0].Required);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void AllModules_ExposeContract_WithMatchingModuleName()
    {
        foreach (var module in ModuleContractTestData.CreateAllModules())
        {
            IModuleContract contract = module.Contract;
            Assert.IsNotNull(contract, $"{module.GetType().Name} must expose a Contract");
            Assert.IsFalse(string.IsNullOrWhiteSpace(contract.ModuleName));
            Assert.IsNotNull(contract.Selection);
            Assert.IsNotNull(contract.Data);
            Assert.IsNotNull(contract.Processing);
        }
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ModuleContracts_HaveExpectedAnatomy()
    {
        var byName = ModuleContractTestData.CreateAllModules()
            .ToDictionary(m => m.Contract.ModuleName, m => m.Contract);

        CollectionAssert.AreEquivalent(
            new[] { "WorkItems", "Teams", "Nodes", "Identities" }, byName.Keys.ToArray());

        var teams = byName["Teams"];
        CollectionAssert.AreEquivalent(new[] { "Scope", "Filter" }, teams.Selection.Select(s => s.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "TeamSettings", "TeamIterations", "TeamMembers", "TeamCapacity" },
            teams.Data.Select(d => d.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "AlwaysExport", "NodeTranslation", "IdentityLookup" },
            teams.Processing.Select(p => p.Name).ToArray());

        CollectionAssert.AreEquivalent(new[] { "ReplicateSourceTree" }, byName["Nodes"].Processing.Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "DefaultIdentity" }, byName["Identities"].Processing.Select(p => p.Name).ToArray());
    }
}

internal static class ModuleContractTestData
{
    /// <summary>
    /// Instantiates concrete platform modules the same way the inventory
    /// integration tests do (reuses <see cref="InventoryModuleFactory"/>).
    /// </summary>
    internal static IEnumerable<IModule> CreateAllModules()
    {
        yield return InventoryModuleFactory.CreateWorkItemsModule(new Mock<IPackageAccess>());
        yield return InventoryModuleFactory.CreateTeamsModule();
        yield return InventoryModuleFactory.CreateNodesModule();
        yield return InventoryModuleFactory.CreateIdentitiesModule();
    }
}
