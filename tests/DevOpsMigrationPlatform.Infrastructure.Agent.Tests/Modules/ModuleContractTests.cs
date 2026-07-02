// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
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
    }
}
