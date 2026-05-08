// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Discovery;

[TestClass]
public sealed class InventoryCompatibilitySemanticsTests
{
    [TestMethod]
    public void InventoryCursorIdentity_UsesActionQualifiedNamespace()
    {
        var identity = StateCursorIdentity.Build("inventory", "workitems");
        Assert.AreEqual("inventory.workitems", identity);
        Assert.IsTrue(StateCursorIdentity.TryParse(identity, out var action, out var module));
        Assert.AreEqual("inventory", action);
        Assert.AreEqual("workitems", module);
    }
}
