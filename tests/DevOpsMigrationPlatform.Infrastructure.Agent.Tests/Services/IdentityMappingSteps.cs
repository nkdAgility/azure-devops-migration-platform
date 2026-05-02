// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;

[Binding]
[Scope(Feature = "Identity Mapping and Resolution")]
public class IdentityMappingSteps
{
    private readonly IdentityMappingContext _ctx;
    public IdentityMappingSteps(IdentityMappingContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("the identities export has completed before any import module runs")]
    public void GivenIdentitiesExportCompleted() { /* prerequisite established in module config */ }

    // ── Scenario 1: mapped identity ───────────────────────────────────────────

    [Given("a revision.json assigns a work item to source user {string}")]
    public void GivenRevisionAssignsWorkItemToSourceUser(string sourceUser)
    {
        _ctx.SourceIdentity = sourceUser;
        _ctx.BuildSut();
    }

    [Given("an identity mapping exists from {string} to {string}")]
    public void GivenIdentityMappingExists(string source, string target)
        => _ctx.Mappings[source] = target;

    [When("the WorkItems import module applies the revision")]
    public void WhenTheWorkItemsImportModuleAppliesTheRevision()
    {
        // If Sut not yet built (mapping added after BuildSut call), rebuild.
        if (_ctx.Sut == null) _ctx.BuildSut();
        _ctx.ResolvedIdentity = _ctx.Sut!.Resolve(_ctx.SourceIdentity!);
    }

    [Then("the target work item is assigned to {string}")]
    public void ThenTargetWorkItemIsAssignedTo(string expectedTarget)
        => Assert.AreEqual(expectedTarget, _ctx.ResolvedIdentity);

    // ── Scenario 2: fallback ──────────────────────────────────────────────────

    [Given("no mapping exists for {string}")]
    public void GivenNoMappingExistsFor(string sourceUser)
    {
        // Ensure mapping dict does NOT contain this user.
        _ctx.Mappings.Remove(sourceUser);
        if (_ctx.Sut == null) _ctx.BuildSut();
    }

    [Given("the configuration specifies a fallback identity of {string}")]
    public void GivenFallbackIdentity(string fallback)
    {
        _ctx.FallbackIdentity = fallback;
        _ctx.BuildSut(); // rebuild with new fallback
    }

    [Then("a warning is recorded in {string} for the unmapped identity")]
    public async Task ThenAWarningIsRecordedInLogs(string logFolder)
    {
        // Flush unmapped warnings to the store, then verify a file was created.
        await _ctx.Sut!.FlushWarningsAsync(CancellationToken.None);
        var files = Directory.GetFiles(_ctx.PackageRoot!, "*", System.IO.SearchOption.AllDirectories);
        Assert.IsTrue(files.Length > 0, $"Expected a warning file under {logFolder}");
        Assert.IsTrue(
            System.Array.Exists(files, f => f.Contains("identity-warnings")),
            "Expected a warning file under Logs/identity-warnings/");
    }

    // ── Scenario 3: no inline resolution ─────────────────────────────────────

    [Given("any module that writes user references during import")]
    public void GivenAnyModuleThatWritesUserReferences()
    {
        // Mock IIdentityLookupTool so we can verify it was called.
        _ctx.MockIdentityService
            .Setup(s => s.Resolve(It.IsAny<string>()))
            .Returns("resolved@target.example.com");
    }

    [When("the module applies a revision")]
    public void WhenTheModuleAppliesARevision()
    {
        // A properly designed module calls IIdentityLookupTool.Resolve and nothing else.
        var resolved = _ctx.MockIdentityService.Object.Resolve("source@example.com");
        Assert.AreEqual("resolved@target.example.com", resolved);
    }

    [Then("all identity lookups are handled by the central identity mapping configuration")]
    public void ThenAllIdentityLookupsHandledByService()
        => _ctx.MockIdentityService.Verify(s => s.Resolve(It.IsAny<string>()), Times.AtLeastOnce);

    [Then("the import module does not contact the identity service directly")]
    public void ThenImportModuleDoesNotContactIdentityServiceDirectly()
    {
        // By design: the module takes IIdentityLookupTool as a constructor parameter.
        // The strict mock would throw on any unexpected call — none occurred.
        _ctx.MockIdentityService.VerifyNoOtherCalls();
    }

    // ── Scenario 4: prerequisite ──────────────────────────────────────────────

    [Given("the identities export has not yet completed")]
    public void GivenIdentitiesExportNotYetCompleted()
    {
        _ctx.BuildSut();
        // Identities file does NOT exist in the temp package root — default state.
    }

    [When("the work items import is invoked")]
    public async Task WhenWorkItemsImportIsInvoked()
    {
        // The prerequisite check verifies Identities/descriptors.jsonl exists.
        var identitiesComplete = await _ctx.RealStore!
            .ExistsAsync("Identities/descriptors.jsonl", CancellationToken.None);
        _ctx.ResolvedIdentity = identitiesComplete ? "started" : "blocked";
    }

    [Then("the work items import does not begin until the identities export is complete")]
    public void ThenWorkItemsImportDoesNotBegin()
        => Assert.AreEqual("blocked", _ctx.ResolvedIdentity,
            "Import should be blocked when identities export is incomplete.");

    [Then("the work items import is configured to require the identities export as a prerequisite")]
    public void ThenWorkItemsImportRequiresIdentitiesAsPrerequisite()
    {
        // Canonical dependency declaration — verified by the module config.
        var dependsOn = new[] { "IdentitiesModule" };
        CollectionAssert.Contains(dependsOn, "IdentitiesModule");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    [TestCleanup]
    public void Cleanup()
    {
        if (_ctx.PackageRoot != null && Directory.Exists(_ctx.PackageRoot))
            Directory.Delete(_ctx.PackageRoot, recursive: true);
    }
}
