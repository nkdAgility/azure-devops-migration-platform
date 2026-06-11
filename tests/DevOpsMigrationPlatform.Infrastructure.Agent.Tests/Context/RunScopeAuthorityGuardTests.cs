// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RunScopeAuthorityGuardTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsRunScopedPath_WhenRunFolderPath_ReturnsTrue()
    {
        Assert.IsTrue(RunScopeAuthorityGuard.IsRunScopedPath(".migration/runs/20260507-110000/plan.json"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void EnsureAuthoritativePath_WhenRunFolderPath_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RunScopeAuthorityGuard.EnsureAuthoritativePath(".migration/runs/20260507-110000/config.json", "resume"));
    }
}
