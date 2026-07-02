// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Infrastructure.Agent.ConnectorCapability;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public class ConnectorCapabilityTests
{
    // (a) connector with capability returns Has(flag) == true
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void StaticProvider_WhenFlagIncluded_ReturnsTrue()
    {
        var provider = new StaticConnectorCapabilityProvider(Cap.BoardColumns);

        Assert.IsTrue(provider.Has(Cap.BoardColumns));
    }

    // (b) connector without capability returns Has(flag) == false
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void StaticProvider_WhenFlagExcluded_ReturnsFalse()
    {
        var provider = new StaticConnectorCapabilityProvider(Cap.BoardColumns);

        Assert.IsFalse(provider.Has(Cap.BoardRows));
    }

    // (c) composite — provider with BoardConfig returns true for BoardColumns, BoardRows, CardRules
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void StaticProvider_WithBoardConfigComposite_ReturnsTrueForGranularFlags()
    {
        var provider = new StaticConnectorCapabilityProvider(Cap.BoardConfig);

        Assert.IsTrue(provider.Has(Cap.BoardColumns), "Expected BoardColumns");
        Assert.IsTrue(provider.Has(Cap.BoardRows), "Expected BoardRows");
        Assert.IsTrue(provider.Has(Cap.CardRules), "Expected CardRules");
        Assert.IsTrue(provider.Has(Cap.BoardConfig), "Expected BoardConfig composite");
    }

    // (d) TFS (None) returns false for every flag including granular flags
    // Simulated via StaticConnectorCapabilityProvider(None) — same semantics as TfsConnectorCapabilityProvider
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TfsProvider_AlwaysReturnsFalseForAllFlags()
    {
        var provider = new StaticConnectorCapabilityProvider(Cap.None);

        Assert.IsFalse(provider.Has(Cap.BoardColumns), "BoardColumns");
        Assert.IsFalse(provider.Has(Cap.BoardRows), "BoardRows");
        Assert.IsFalse(provider.Has(Cap.CardRules), "CardRules");
        Assert.IsFalse(provider.Has(Cap.Backlogs), "Backlogs");
        Assert.IsFalse(provider.Has(Cap.TaskboardColumns), "TaskboardColumns");
        Assert.IsFalse(provider.Has(Cap.BoardConfig), "BoardConfig composite");
    }

    // EC-H1 / ADR-0024: team and comment extension capabilities are explicit flags —
    // connectors declare them; consumers gate on Has(flag) instead of null-checking
    // optional seam members.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ConnectorCapability_DefinesTeamAndCommentExtensionFlags()
    {
        foreach (var name in new[] { "TeamSettings", "TeamIterations", "TeamMembers", "TeamCapacity", "TeamAreaPaths", "WorkItemComments" })
        {
            Assert.IsTrue(
                System.Enum.TryParse<Cap>(name, out var flag) && flag != Cap.None,
                $"ConnectorCapability must define a non-zero '{name}' flag (EC-H1).");
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SimulatedAndAzureDevOpsConnectors_DeclareTeamAndCommentCapabilities()
    {
        // Connector-coverage: Simulated and AzureDevOpsServices support the team/comment
        // seams, so their capability registrations must declare the flags. TFS Object Model
        // declares None explicitly via TfsConnectorCapabilityProvider (existing pattern).
        var repoRoot = FindRepoRoot();
        var registrations = new[]
        {
            System.IO.Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.Infrastructure.Simulated", "SimulatedServiceCollectionExtensions.cs"),
            System.IO.Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.Infrastructure.AzureDevOps", "WorkItems", "Revisions", "ExportServiceCollectionExtensions.cs"),
        };

        foreach (var file in registrations)
        {
            var source = System.IO.File.ReadAllText(file);
            foreach (var flag in new[] { "TeamSettings", "TeamIterations", "TeamMembers", "TeamCapacity", "TeamAreaPaths", "WorkItemComments" })
            {
                StringAssert.Contains(source, $"ConnectorCapability.{flag}",
                    $"{System.IO.Path.GetFileName(file)} must declare ConnectorCapability.{flag} (EC-H1).");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repository root.");
        return dir!.FullName;
    }
}
