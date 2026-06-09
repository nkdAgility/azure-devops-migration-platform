// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class ActionQualifiedCursorIdentityTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_WithDifferentActions_ProducesDifferentKeys()
    {
        var exportKey = StateCursorIdentity.Build("export", "workitems");
        var importKey = StateCursorIdentity.Build("import", "workitems");
        Assert.AreNotEqual(exportKey, importKey);
    }
}
