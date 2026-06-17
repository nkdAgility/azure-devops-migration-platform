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
}
