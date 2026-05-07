// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RunAuditInspectabilityTests
{
    [TestMethod]
    public void RunAuditPath_IsInspectable_ButNotAuthoritative()
    {
        var runPlan = PackagePaths.RunPlanFile("20260507-120000");
        StringAssert.Contains(runPlan, ".migration/runs/");
        Assert.IsTrue(RunScopeAuthorityGuard.IsRunScopedPath(runPlan));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RunScopeAuthorityGuard.EnsureAuthoritativePath(runPlan, "phase-gate"));
    }
}
