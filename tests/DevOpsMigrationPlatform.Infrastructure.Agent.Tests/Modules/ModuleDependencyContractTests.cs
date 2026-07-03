// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// MC-L2 contract tests: <see cref="ModuleDependency"/> targets are constrained by phase —
/// module-phase dependencies (Inventory/Export/Import/Both/Prepare) must target
/// <see cref="IModule"/> types, and Analyse-phase dependencies (the dedicated
/// analyser-ordering mechanism) must target <see cref="Abstractions.Agent.Analysis.IAnalyser"/> types.
/// The WorkItems → InventoryAnalyser ordering is expressed through the Analyse phase,
/// not encoded as a module import dependency.
/// </summary>
[TestClass]
public sealed class ModuleDependencyContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ModuleDependency_ImportPhase_ModuleTarget_IsAllowed()
    {
        var dependency = new ModuleDependency(typeof(NodesModule), DependencyPhase.Import);
        Assert.AreEqual("Nodes", dependency.ModuleName);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ModuleDependency_ImportPhase_AnalyserTarget_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Import));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ModuleDependency_ImportPhase_ArbitraryType_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new ModuleDependency(typeof(string), DependencyPhase.Import));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ModuleDependency_AnalysePhase_AnalyserTarget_IsAllowed()
    {
        var dependency = new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Analyse);
        Assert.AreEqual("Inventory", dependency.ModuleName);
        Assert.IsTrue(dependency.AppliesToAnalyse);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ModuleDependency_AnalysePhase_ModuleTarget_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new ModuleDependency(typeof(NodesModule), DependencyPhase.Analyse));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemsModule_DependsOn_UsesAnalysePhaseForInventoryAnalyser()
    {
        var module = new WorkItemsModule(Mock.Of<IWorkItemsOrchestrator>());

        var moduleImports = module.DependsOn
            .Where(d => d.AppliesToImport)
            .Select(d => d.ModuleName)
            .ToList();
        CollectionAssert.AreEquivalent(new[] { "Identities", "Nodes" }, moduleImports,
            "Import-phase dependencies must be module-only (MC-L2).");

        var analyseDeps = module.DependsOn
            .Where(d => d.AppliesToAnalyse)
            .Select(d => d.ModuleName)
            .ToList();
        CollectionAssert.AreEquivalent(new[] { "Inventory" }, analyseDeps,
            "InventoryAnalyser ordering must be expressed through the Analyse-phase mechanism.");
    }
}
