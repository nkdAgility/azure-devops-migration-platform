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
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RunAuditPath_IsInspectable_ButNotAuthoritative()
    {
        var runPlan = PackagePathTestHelper.RunPlanFile("20260507-120000");
        StringAssert.Contains(runPlan, ".migration/runs/");
        Assert.IsTrue(RunScopeAuthorityGuard.IsRunScopedPath(runPlan));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RunScopeAuthorityGuard.EnsureAuthoritativePath(runPlan, "phase-gate"));
    }

    /// <summary>
    /// Scenario: Resume_UsesAuthoritativeScopes_RunScopeIgnored
    /// Given a package contains root and project migration state
    /// And the run audit folder contains stale copies of those files
    /// When a migration job evaluates resume and phase gates
    /// Then only root and project scoped files are used as authoritative state
    /// And run-scope files remain inspectable audit artefacts only
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resume_UsesAuthoritativeScopes_RunScopeIgnored()
    {
        // Authoritative paths (root and project scoped)
        var rootPlanFile = PackagePathTestHelper.PlanFile;
        var rootPhaseFile = PackagePathTestHelper.PhaseFile;
        var projectCursorFile = PackagePathTestHelper.CursorFile("export", "workitems",
            "https://dev.azure.com/contoso", "Shop");

        // Run-audit stale copies of those same files
        const string runId = "20260507-130000";
        var runAuditPlan = PackagePathTestHelper.RunPlanFile(runId);
        var runAuditCursor = $".migration/runs/{runId}/audit/export.workitems.cursor.json";

        // Root and project scoped paths are authoritative — EnsureAuthoritativePath does not throw
        RunScopeAuthorityGuard.EnsureAuthoritativePath(rootPlanFile, "phase-gate");
        RunScopeAuthorityGuard.EnsureAuthoritativePath(rootPhaseFile, "resume");
        RunScopeAuthorityGuard.EnsureAuthoritativePath(projectCursorFile, "resume");

        // Run-audit copies are identifiable as run-scoped and cannot be used as authoritative state
        Assert.IsTrue(RunScopeAuthorityGuard.IsRunScopedPath(runAuditPlan),
            "Run audit plan copy must be identified as run-scoped.");
        Assert.IsTrue(RunScopeAuthorityGuard.IsRunScopedPath(runAuditCursor),
            "Run audit cursor copy must be identified as run-scoped.");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RunScopeAuthorityGuard.EnsureAuthoritativePath(runAuditPlan, "phase-gate"),
            "Run-scope plan copy must be rejected for phase-gate use.");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RunScopeAuthorityGuard.EnsureAuthoritativePath(runAuditCursor, "resume"),
            "Run-scope cursor copy must be rejected for resume use.");

        // Authoritative paths must NOT be flagged as run-scoped
        Assert.IsFalse(RunScopeAuthorityGuard.IsRunScopedPath(rootPlanFile),
            "Root plan file must not be classified as run-scoped.");
        Assert.IsFalse(RunScopeAuthorityGuard.IsRunScopedPath(projectCursorFile),
            "Project cursor file must not be classified as run-scoped.");
    }
}
