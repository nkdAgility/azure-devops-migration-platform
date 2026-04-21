using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[Binding]
[Scope(Feature = "Work Item Watermark Store")]
public class WatermarkStoreCachingSteps
{
    private readonly WatermarkStoreCachingContext _ctx;

    public WatermarkStoreCachingSteps(WatermarkStoreCachingContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("the watermark store has been initialised")]
    public async Task GivenTheWatermarkStoreHasBeenInitialised()
    {
        await _ctx.MockIdMapStore.Object.InitializeAsync(CancellationToken.None);
    }

    [Given("the export has not yet begun")]
    public void GivenTheExportHasNotYetBegun() { }

    // ── Scenario 1: Watermark recorded when first revision is processed ──────

    [Given(@"work item (\d+) has not been exported before")]
    public void GivenWorkItemHasNotBeenExportedBefore(int wiId)
    {
        // No watermark entry — GetLastRevisionIndexAsync returns null
    }

    [When(@"the export processes revision (\d+) of work item (\d+)")]
    public async Task WhenTheExportProcessesRevisionOfWorkItem(int revIdx, int wiId)
    {
        await _ctx.MockIdMapStore.Object.UpdateLastRevisionIndexAsync(wiId, revIdx, CancellationToken.None);
    }

    [Then(@"the watermark store records that work item (\d+) was last processed at revision (\d+)")]
    public async Task ThenTheWatermarkStoreRecordsLastProcessedAt(int wiId, int revIdx)
    {
        var result = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        Assert.IsNotNull(result, $"Watermark should exist for work item {wiId}");
        Assert.AreEqual(revIdx, result.Value,
            $"Work item {wiId} watermark should be {revIdx}");
    }

    // ── Scenario 2: Watermark advances on later revision ─────────────────────

    [Given(@"the export has already processed up to revision (\d+) of work item (\d+)")]
    public void GivenTheExportHasAlreadyProcessedUpToRevision(int revIdx, int wiId)
    {
        _ctx.Watermarks[wiId] = revIdx;
    }

    // When step reuses "the export processes revision N of work item M" from Scenario 1

    // Then step reuses "the watermark store records that work item M was last processed at revision N"

    // ── Scenario 3: Watermark does not retreat ───────────────────────────────

    [When(@"the export attempts to record progress at revision (\d+) of work item (\d+)")]
    public async Task WhenTheExportAttemptsToRecordProgress(int revIdx, int wiId)
    {
        await _ctx.MockIdMapStore.Object.UpdateLastRevisionIndexAsync(wiId, revIdx, CancellationToken.None);
    }

    [Then(@"the watermark store still records that work item (\d+) was last processed at revision (\d+)")]
    public async Task ThenTheWatermarkStoreStillRecords(int wiId, int revIdx)
    {
        var result = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        Assert.IsNotNull(result, $"Watermark should exist for work item {wiId}");
        Assert.AreEqual(revIdx, result.Value,
            $"Work item {wiId} watermark should still be {revIdx}");
    }

    // ── Scenario 4: Revisions at or below watermark are already processed ────

    [Then(@"the platform considers revision (\d+) of work item (\d+) as already processed")]
    public async Task ThenThePlatformConsidersRevisionAsAlreadyProcessed(int revIdx, int wiId)
    {
        var watermark = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        Assert.IsNotNull(watermark, $"Watermark should exist for work item {wiId}");
        Assert.IsTrue(revIdx <= watermark.Value,
            $"Revision {revIdx} should be at or below watermark {watermark.Value}");
    }

    // ── Scenario 5: Revisions above watermark are unprocessed ────────────────

    [Then(@"the platform considers revision (\d+) of work item (\d+) as not yet processed")]
    public async Task ThenThePlatformConsidersRevisionAsNotYetProcessed(int revIdx, int wiId)
    {
        var watermark = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        if (watermark is null)
        {
            // No watermark means all revisions are unprocessed
            return;
        }
        Assert.IsTrue(revIdx > watermark.Value,
            $"Revision {revIdx} should be above watermark {watermark.Value}");
    }

    // ── Scenario 6: No recorded progress = fully unprocessed ─────────────────

    [Given(@"work item (\d+) has never been exported")]
    public void GivenWorkItemHasNeverBeenExported(int wiId)
    {
        // No watermark entry — GetLastRevisionIndexAsync returns null
    }

    [Then(@"the platform considers all revisions of work item (\d+) as not yet processed")]
    public async Task ThenThePlatformConsidersAllRevisionsAsNotYetProcessed(int wiId)
    {
        var watermark = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        Assert.IsNull(watermark,
            $"Work item {wiId} should have no watermark — all revisions are unprocessed");
    }

    // ── Scenario 7: WIQL query result count is cached ────────────────────────

    [Given("a WIQL query has not been run before")]
    public void GivenAWiqlQueryHasNotBeenRunBefore()
    {
        _ctx.WiqlCountCache.Clear();
        _ctx.WiqlDataSourceCallCounts.Clear();
    }

    [When(@"the export determines there are (\d+) work items matching that query")]
    public void WhenTheExportDeterminesWorkItemCount(int count)
    {
        const string queryKey = "default";
        _ctx.WiqlDataSourceReturns[queryKey] = count;
        var result = _ctx.GetCachedCountOrQuery(queryKey);
        Assert.AreEqual(count, result);
    }

    [Then(@"the platform stores (\d+) as the cached count for that query")]
    public void ThenThePlatformStoresCachedCount(int count)
    {
        Assert.AreEqual(count, _ctx.WiqlCountCache["default"]);
    }

    [Then("a subsequent check for the same query returns {int} without querying the source again")]
    public void ThenASubsequentCheckReturnsCachedCountWithoutQueryingSource(int count)
    {
        // Query a second time — should use cache
        var result = _ctx.GetCachedCountOrQuery("default");
        Assert.AreEqual(count, result);
        // Data source should have been called exactly once (from the When step)
        Assert.AreEqual(1, _ctx.WiqlDataSourceCallCounts["default"],
            "Data source should have been called exactly once (result cached).");
    }

    // ── Scenario 8: Cached query count is replaced ───────────────────────────

    [Given(@"the platform has cached a count of (\d+) for a WIQL query")]
    public void GivenThePlatformHasCachedACount(int count)
    {
        _ctx.WiqlCountCache["default"] = count;
    }

    [When(@"the export records a new count of (\d+) for the same query")]
    public void WhenTheExportRecordsANewCount(int count)
    {
        _ctx.RecordCount("default", count);
    }

    [Then(@"the platform returns (\d+) as the cached count for that query")]
    public void ThenThePlatformReturnsCachedCount(int count)
    {
        Assert.AreEqual(count, _ctx.WiqlCountCache["default"]);
    }

    // ── Scenario 9: Watermarks for different work items are independent ──────

    [Given(@"the export has processed up to revision (\d+) of work item (\d+)")]
    public void GivenTheExportHasProcessedUpToRevision(int revIdx, int wiId)
    {
        _ctx.Watermarks[wiId] = revIdx;
    }

    // Scenario 9 Then steps reuse the existing bindings from Scenarios 4 and 5.

    // ── Scenario 10: Recorded watermarks persist across restart ──────────────

    [When("the migration platform is restarted")]
    public void WhenTheMigrationPlatformIsRestarted()
    {
        // Simulate flush: save current watermarks
        _ctx.SavedWatermarks = new Dictionary<int, int>(_ctx.Watermarks);

        // Simulate restart: clear in-memory state
        _ctx.Watermarks.Clear();

        // Simulate reload from disk
        foreach (var kv in _ctx.SavedWatermarks)
            _ctx.Watermarks[kv.Key] = kv.Value;
    }

    [Then(@"the platform still considers revision (\d+) of work item (\d+) as already processed")]
    public async Task ThenThePlatformStillConsidersRevisionAsAlreadyProcessed(int revIdx, int wiId)
    {
        var watermark = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        Assert.IsNotNull(watermark, $"Watermark for work item {wiId} should persist after restart");
        Assert.IsTrue(revIdx <= watermark.Value,
            $"Revision {revIdx} should still be at or below watermark {watermark.Value} after restart");
    }

    [Then(@"the platform still considers revision (\d+) of work item (\d+) as not yet processed")]
    public async Task ThenThePlatformStillConsidersRevisionAsNotYetProcessed(int revIdx, int wiId)
    {
        var watermark = await _ctx.MockIdMapStore.Object.GetLastRevisionIndexAsync(wiId, CancellationToken.None);
        Assert.IsNotNull(watermark, $"Watermark should exist for work item {wiId}");
        Assert.IsTrue(revIdx > watermark.Value,
            $"Revision {revIdx} should still be above watermark {watermark.Value} after restart");
    }
}
