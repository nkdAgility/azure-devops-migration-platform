// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class WorkItemsModuleArchitectureTests
{
    [TestMethod]
    public void WorkItemsModule_DoesNotInlineConstructWorkItemExportOrchestrator()
    {
        var repoRoot = GetRepositoryRoot();
        var modulePath = Path.Combine(
            repoRoot,
            "src",
            "DevOpsMigrationPlatform.Infrastructure.Agent",
            "Modules",
            "WorkItemsModule.cs");

        var source = File.ReadAllText(modulePath);
        Assert.IsFalse(
            source.Contains("new WorkItemExportOrchestrator(", StringComparison.Ordinal),
            "WorkItemsModule must consume an export orchestrator abstraction instead of inlining concrete orchestrator construction.");
    }

    [TestMethod]
    public void WorkItemsModule_UsesSingleWorkItemsOrchestratorAbstraction()
    {
        var repoRoot = GetRepositoryRoot();
        var modulePath = Path.Combine(
            repoRoot,
            "src",
            "DevOpsMigrationPlatform.Infrastructure.Agent",
            "Modules",
            "WorkItemsModule.cs");

        var source = File.ReadAllText(modulePath);
        Assert.IsFalse(
            source.Contains("IWorkItemsImportOrchestrator", StringComparison.Ordinal),
            "WorkItemsModule should depend on the single IWorkItemsOrchestrator abstraction.");
        Assert.IsTrue(
            source.Contains("IWorkItemsOrchestrator", StringComparison.Ordinal),
            "WorkItemsModule should depend on IWorkItemsOrchestrator.");
    }

    [TestMethod]
    public void WorkItemsModule_DoesNotInlineConstructWorkItemOrchestrator()
    {
        var repoRoot = GetRepositoryRoot();
        var modulePath = Path.Combine(
            repoRoot,
            "src",
            "DevOpsMigrationPlatform.Infrastructure.Agent",
            "Modules",
            "WorkItemsModule.cs");

        var source = File.ReadAllText(modulePath);
        Assert.IsFalse(
            source.Contains("new WorkItemsImportRuntime(", StringComparison.Ordinal),
            "WorkItemsModule must consume an import orchestrator abstraction/factory instead of inlining concrete orchestrator construction.");
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DevOpsMigrationPlatform.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
