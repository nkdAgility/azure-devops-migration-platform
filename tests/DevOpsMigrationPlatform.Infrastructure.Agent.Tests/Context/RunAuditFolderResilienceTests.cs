// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RunAuditFolderResilienceTests
{
    [TestMethod]
    public void IsRunScopedPath_WithMissingOrMalformedPath_DoesNotThrow()
    {
        Assert.IsFalse(RunScopeAuthorityGuard.IsRunScopedPath(string.Empty));
        Assert.IsFalse(RunScopeAuthorityGuard.IsRunScopedPath("plan.json"));
        Assert.IsTrue(RunScopeAuthorityGuard.IsRunScopedPath(@".migration\runs\id\job.json"));
    }
}
