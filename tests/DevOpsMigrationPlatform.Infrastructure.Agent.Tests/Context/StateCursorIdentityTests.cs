// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class StateCursorIdentityTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_ReturnsLowercaseActionQualifiedIdentity()
    {
        var identity = StateCursorIdentity.Build("Export", "WorkItems");
        Assert.AreEqual("export.workitems", identity);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TryParse_ActionQualifiedValue_ReturnsActionAndModule()
    {
        var ok = StateCursorIdentity.TryParse("inventory.nodes", out var action, out var module);
        Assert.IsTrue(ok);
        Assert.AreEqual("inventory", action);
        Assert.AreEqual("nodes", module);
    }
}
