// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Validates that module constructors accept only their own isolated config slice
/// and do not receive the full platform options graph. (module-isolation feature family)
/// </summary>
[TestClass]
public sealed class ModuleIsolationTests
{
    // ── Scenario: ModuleConstructed_IsolatedOptions_NoFullGraph ─────────────

    /// <summary>
    /// ADR 0019: <c>WorkItemsModule</c> is a thin façade taking only <c>IWorkItemsOrchestrator</c>;
    /// it holds no logic and receives no config. The isolated options slice
    /// (<c>IOptions&lt;WorkItemsModuleOptions&gt;</c>, not the full platform graph) now lives on
    /// <c>WorkItemsOrchestrator</c>, which is composed by DI.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemsModule_Constructor_ReceivesIsolatedOptionsSlice_NotFullGraph()
    {
        // The thin module takes only the orchestrator and no platform graph.
        var moduleCtor = typeof(DevOpsMigrationPlatform.Infrastructure.Agent.Modules.WorkItemsModule)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();
        var moduleParamTypes = moduleCtor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.IsTrue(
            moduleParamTypes.Any(t => t == typeof(DevOpsMigrationPlatform.Abstractions.Agent.Modules.IWorkItemsOrchestrator)),
            "Thin WorkItemsModule must accept IWorkItemsOrchestrator.");
        Assert.IsFalse(
            moduleParamTypes.Any(t => t == typeof(MigrationPlatformOptions)),
            "WorkItemsModule must NOT receive the full MigrationPlatformOptions graph.");

        // The orchestrator (the real owner) receives the isolated options slice + endpoint info,
        // and never the full platform graph.
        var orchestratorCtor = typeof(DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution.WorkItemsOrchestrator)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();
        var orchestratorParamTypes = orchestratorCtor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.IsTrue(
            orchestratorParamTypes.Any(t => t == typeof(IOptions<WorkItemsModuleOptions>)),
            "WorkItemsOrchestrator must accept IOptions<WorkItemsModuleOptions>.");
        Assert.IsTrue(
            orchestratorParamTypes.Any(t => t == typeof(ISourceEndpointInfo)),
            "WorkItemsOrchestrator must accept ISourceEndpointInfo.");
        Assert.IsFalse(
            orchestratorParamTypes.Any(t => t == typeof(MigrationPlatformOptions)),
            "WorkItemsOrchestrator must NOT receive the full MigrationPlatformOptions graph.");
    }

    // ── Scenario: ModuleUnitTest_IsolatedOptions_MinimalDependencies ────────

    /// <summary>
    /// The WorkItemsModule source file does not reference other modules' options types,
    /// demonstrating that unit-testing it requires only WorkItemsModuleOptions.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemsModule_SourceFile_DoesNotReferenceOtherModuleOptionsTypes()
    {
        var repoRoot = GetRepositoryRoot();
        var modulePath = Path.Combine(
            repoRoot,
            "src",
            "DevOpsMigrationPlatform.Infrastructure.Agent",
            "Modules",
            "WorkItemsModule.cs");

        var source = File.ReadAllText(modulePath);

        // WorkItemsModule should not directly reference sibling module options
        Assert.IsFalse(
            source.Contains("TeamsModuleOptions", StringComparison.Ordinal),
            "WorkItemsModule must not reference TeamsModuleOptions — modules must be independently testable.");

        Assert.IsFalse(
            source.Contains("IdentitiesModuleOptions", StringComparison.Ordinal),
            "WorkItemsModule must not reference IdentitiesModuleOptions — modules must be independently testable.");

        // NodesModuleOptions is a legitimate optional dependency for node readiness — allowed
    }

    // ── Scenario: DuplicateSectionName_DIRegistration_FailsAtStartup ────────

    /// <summary>
    /// All module options types that expose SectionName have unique values,
    /// so duplicate registration would be caught at startup rather than silently overwriting config.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AllModuleOptions_SectionNames_AreUnique()
    {
        // Collect all public concrete types in the Abstractions assembly that have a static SectionName property
        // Exclude interfaces and abstract types — static abstract interface members cannot be invoked reflectively
        var abstractionsAssembly = typeof(WorkItemsModuleOptions).Assembly;
        var sectionNames = abstractionsAssembly
            .GetExportedTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Select(t => t.GetProperty("SectionName", BindingFlags.Public | BindingFlags.Static))
            .Where(p => p is not null && p.PropertyType == typeof(string))
            .Select(p => (string?)p!.GetValue(null))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        var duplicates = sectionNames
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.AreEqual(0, duplicates.Length,
            $"Duplicate SectionName values detected — DI registration would fail at startup: {string.Join(", ", duplicates!)}");
    }

    // ── Scenario: NewModule_FollowsPattern_ExplicitContract ─────────────────

    /// <summary>
    /// Every module options type in the Abstractions assembly exposes a static SectionName
    /// constant, satisfying the isolated injection pattern contract.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AllModuleOptions_HaveStaticSectionName()
    {
        var abstractionsAssembly = typeof(WorkItemsModuleOptions).Assembly;

        // Find concrete classes whose name ends in "ModuleOptions"
        var moduleOptionsTypes = abstractionsAssembly
            .GetExportedTypes()
            .Where(t => t.Name.EndsWith("ModuleOptions", StringComparison.Ordinal) && t.IsClass && !t.IsAbstract)
            .ToArray();

        Assert.IsTrue(moduleOptionsTypes.Length > 0, "Expected at least one *ModuleOptions type in Abstractions assembly.");

        foreach (var type in moduleOptionsTypes)
        {
            var sectionNameProp = type.GetProperty("SectionName", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(sectionNameProp,
                $"{type.Name} must expose a static SectionName property to satisfy the isolated injection pattern contract.");

            var value = (string?)sectionNameProp.GetValue(null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(value),
                $"{type.Name}.SectionName must not be null or empty.");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DevOpsMigrationPlatform.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
