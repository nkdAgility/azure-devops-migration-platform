// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
}
