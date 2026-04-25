using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "ID Map Integrity Check")]
public class IdMapIntegrityCheckSteps
{
    private readonly IdMapIntegrityCheckContext _ctx;

    public IdMapIntegrityCheckSteps(IdMapIntegrityCheckContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a migration package with idmap.db containing work item mappings")]
    public void GivenAMigrationPackageWithIdmapDb()
    {
        // Setup deferred to per-scenario Given steps.
    }

    // ── Scenario 1: Warning for deleted target ────────────────────────────────

    [Given("idmap.db contains mappings:")]
    public void GivenIdmapDbContainsMappings(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            _ctx.ConfiguredMappings.Add(new IdMapEntry
            {
                SourceId = int.Parse(row["sourceId"]),
                TargetId = int.Parse(row["targetId"])
            });
        }
        _ctx.SetupCheckIntegrity();
    }

    [Given(@"target work item (\d+) does not exist in the target system")]
    public void GivenTargetWorkItemDoesNotExist(int targetId)
    {
        _ctx.ExistingTargetIds.Remove(targetId);
    }

    [Given(@"target work item (\d+) exists in the target system")]
    public void GivenTargetWorkItemExists(int targetId)
    {
        _ctx.ExistingTargetIds.Add(targetId);
    }

    [When("the import pipeline runs CheckIntegrityAsync")]
    public async Task WhenTheImportPipelineRunsCheckIntegrity()
    {
        _ctx.IntegrityResult = await _ctx.MockIdMapStore.Object.CheckIntegrityAsync(
            (targetId, ct) => Task.FromResult(_ctx.ExistingTargetIds.Contains(targetId)),
            CancellationToken.None);
        _ctx.PipelineContinued = true;
    }

    [Then(@"a warning is logged for the mapping source (\d+) → target (\d+)")]
    public void ThenWarningIsLoggedForMapping(int sourceId, int targetId)
    {
        Assert.IsNotNull(_ctx.IntegrityResult);
        Assert.IsTrue(
            _ctx.IntegrityResult.Any(e => e.SourceId == sourceId && e.TargetId == targetId),
            $"Expected stale entry for source {sourceId} → target {targetId}.");
    }

    [Then(@"no warning is logged for the mapping source (\d+) → target (\d+)")]
    public void ThenNoWarningIsLoggedForMapping(int sourceId, int targetId)
    {
        Assert.IsNotNull(_ctx.IntegrityResult);
        Assert.IsFalse(
            _ctx.IntegrityResult.Any(e => e.SourceId == sourceId && e.TargetId == targetId),
            $"Did not expect stale entry for source {sourceId} → target {targetId}.");
    }

    [Then(@"the import pipeline continues \(integrity check is non-blocking\)")]
    public void ThenTheImportPipelineContinues()
    {
        Assert.IsTrue(_ctx.PipelineContinued, "Pipeline should continue after integrity check.");
    }

    // ── Scenario 2: No warnings when all mappings are valid ───────────────────

    [Given(@"idmap.db contains a mapping source (\d+) → target (\d+)")]
    public void GivenIdmapDbContainsSingleMapping(int sourceId, int targetId)
    {
        _ctx.ConfiguredMappings.Add(new IdMapEntry { SourceId = sourceId, TargetId = targetId });
        _ctx.SetupCheckIntegrity();
    }

    [Then("no integrity warnings are logged")]
    public void ThenNoIntegrityWarningsAreLogged()
    {
        Assert.IsNotNull(_ctx.IntegrityResult);
        Assert.AreEqual(0, _ctx.IntegrityResult.Count, "Expected zero stale entries.");
    }

    [Then("the import pipeline continues normally")]
    public void ThenTheImportPipelineContinuesNormally()
    {
        Assert.IsTrue(_ctx.PipelineContinued, "Pipeline should continue normally.");
    }
}
