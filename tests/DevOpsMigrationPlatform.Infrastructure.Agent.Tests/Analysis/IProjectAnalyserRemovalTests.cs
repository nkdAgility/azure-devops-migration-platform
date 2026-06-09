// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Analysis;

/// <summary>
/// Verifies that IProjectAnalyser has been fully removed from the codebase (US3).
/// These tests encode architectural intent as executable constraints.
/// </summary>
[TestClass]
public sealed class IProjectAnalyserRemovalTests
{
    /// <summary>
    /// Scenario: Solution_AfterRefactor_ContainsNoIProjectAnalyserReferences
    /// Verifies that no type named IProjectAnalyser exists in any loaded assembly from
    /// the solution's src or tests output.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Solution_AfterRefactor_ContainsNoIProjectAnalyserReferences()
    {
        // All assemblies reachable from the test runner represent compiled solution output.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .ToArray();

        // Look only for a type named exactly "IProjectAnalyser" (the removed interface),
        // not for test helpers or classes whose names merely contain the substring.
        var iProjectAnalyserTypes = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>(); }
            })
            .Where(t => t.Name.Equals("IProjectAnalyser", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(
            0,
            iProjectAnalyserTypes.Length,
            $"Expected IProjectAnalyser to be fully removed but found: {string.Join(", ", iProjectAnalyserTypes.Select(t => t.FullName))}");
    }

    /// <summary>
    /// Scenario: DependencyAnalyser_ClassDeclaration_ImplementsOnlyIOrganisationsAnalyser
    /// Verifies that DependencyAnalyser implements IOrganisationsAnalyser and does not
    /// implement any per-project capture interface (IProjectAnalyser or similar).
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void DependencyAnalyser_ClassDeclaration_ImplementsIOrganisationsAnalyser()
    {
        var type = typeof(DependencyAnalyser);
        Assert.IsTrue(
            typeof(IOrganisationsAnalyser).IsAssignableFrom(type),
            "DependencyAnalyser must implement IOrganisationsAnalyser.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void DependencyAnalyser_ClassDeclaration_DoesNotImplementIProjectAnalyser()
    {
        var type = typeof(DependencyAnalyser);

        var perProjectInterfaces = type.GetInterfaces()
            .Where(i => i.Name.Contains("IProjectAnalyser", StringComparison.Ordinal)
                     || i.Name.Contains("PerProject", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(
            0,
            perProjectInterfaces.Length,
            $"DependencyAnalyser must not implement any per-project capture interface but found: {string.Join(", ", perProjectInterfaces.Select(i => i.FullName))}");
    }
}
