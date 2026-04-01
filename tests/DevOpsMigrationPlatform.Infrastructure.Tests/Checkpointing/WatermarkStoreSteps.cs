using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[Binding]
public class WatermarkStoreSteps
{
    private readonly WatermarkStoreContext _ctx;

    public WatermarkStoreSteps(WatermarkStoreContext ctx)
    {
        _ctx = ctx;
    }

    // ── Background ───────────────────────────────────────────────────────────

    [Given("the watermark store has been initialised")]
    public void GivenTheWatermarkStoreHasBeenInitialised()
    {
        _ctx.StoreDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_ctx.StoreDirectory);
        _ctx.Sut = new FileSystemWorkItemWatermarkStore(new FileSystemStateStore(_ctx.StoreDirectory));
    }

    [Given("the export has not yet begun")]
    public void GivenTheExportHasNotYetBegun()
    {
        // No watermarks recorded — fresh store.
    }

    // ── Scenario 1 ───────────────────────────────────────────────────────────

    [Given(@"work item (\d+) has not been exported before")]
    public async Task GivenWorkItemHasNotBeenExportedBefore(int workItemId)
    {
        var wm = await _ctx.Sut!.GetWatermarkAsync(workItemId, CancellationToken.None);
        Assert.IsNull(wm, $"Work item {workItemId} should have no watermark yet.");
    }

    [When(@"the export processes revision (\d+) of work item (\d+)")]
    public async Task WhenTheExportProcessesRevisionOfWorkItem(int revision, int workItemId)
    {
        await _ctx.Sut!.UpdateWatermarkAsync(workItemId, revision, CancellationToken.None);
        _ctx.Watermarks[workItemId] = revision;
    }

    [Then(@"the watermark store records that work item (\d+) was last processed at revision (\d+)")]
    [Then(@"the watermark store still records that work item (\d+) was last processed at revision (\d+)")]
    public async Task ThenTheWatermarkStoreRecordsThatWorkItemWasLastProcessedAtRevision(int workItemId, int revision)
    {
        var wm = await _ctx.Sut!.GetWatermarkAsync(workItemId, CancellationToken.None);
        Assert.IsNotNull(wm);
        Assert.AreEqual(revision, wm.Value);
    }

    // ── Scenario 2 ───────────────────────────────────────────────────────────

    [Given(@"the export has already processed up to revision (\d+) of work item (\d+)")]
    [Given(@"the export has processed up to revision (\d+) of work item (\d+)")]
    public async Task GivenTheExportHasAlreadyProcessedUpToRevisionOfWorkItem(int revision, int workItemId)
    {
        await _ctx.Sut!.UpdateWatermarkAsync(workItemId, revision, CancellationToken.None);
        _ctx.Watermarks[workItemId] = revision;
    }

    // ── Scenario 3 ───────────────────────────────────────────────────────────

    [When(@"the export attempts to record progress at revision (\d+) of work item (\d+)")]
    public async Task WhenTheExportAttemptsToRecordProgressAtRevisionOfWorkItem(int revision, int workItemId)
    {
        await _ctx.Sut!.UpdateWatermarkAsync(workItemId, revision, CancellationToken.None);
    }

    // ── Scenarios 4 & 5 ──────────────────────────────────────────────────────

    [Then(@"the platform considers revision (\d+) of work item (\d+) as already processed")]
    public async Task ThenThePlatformConsidersRevisionOfWorkItemAsAlreadyProcessed(int revision, int workItemId)
    {
        var processed = await _ctx.Sut!.IsRevisionProcessedAsync(workItemId, revision, CancellationToken.None);
        Assert.IsTrue(processed, $"Revision {revision} of work item {workItemId} should be marked processed.");
    }

    [Then(@"the platform considers revision (\d+) of work item (\d+) as not yet processed")]
    public async Task ThenThePlatformConsidersRevisionOfWorkItemAsNotYetProcessed(int revision, int workItemId)
    {
        var processed = await _ctx.Sut!.IsRevisionProcessedAsync(workItemId, revision, CancellationToken.None);
        Assert.IsFalse(processed, $"Revision {revision} of work item {workItemId} should NOT be marked processed.");
    }

    // ── Scenario 6 ───────────────────────────────────────────────────────────

    [Given(@"work item (\d+) has never been exported")]
    public async Task GivenWorkItemHasNeverBeenExported(int workItemId)
    {
        var wm = await _ctx.Sut!.GetWatermarkAsync(workItemId, CancellationToken.None);
        Assert.IsNull(wm);
    }

    [Then(@"the platform considers all revisions of work item (\d+) as not yet processed")]
    public async Task ThenThePlatformConsidersAllRevisionsOfWorkItemAsNotYetProcessed(int workItemId)
    {
        foreach (var rev in new[] { 0, 1, 5, 100 })
        {
            var processed = await _ctx.Sut!.IsRevisionProcessedAsync(workItemId, rev, CancellationToken.None);
            Assert.IsFalse(processed, $"Revision {rev} of work item {workItemId} should not be processed.");
        }
    }

    // ── Scenario 7 ───────────────────────────────────────────────────────────

    [Given("a WIQL query has not been run before")]
    public async Task GivenAWiqlQueryHasNotBeenRunBefore()
    {
        var count = await _ctx.Sut!.GetQueryCountAsync("SELECT [System.Id] FROM WorkItems", CancellationToken.None);
        Assert.IsNull(count);
    }

    [When(@"the export determines there are (\d+) work items matching that query")]
    public async Task WhenTheExportDeterminesThereAreWorkItemsMatchingThatQuery(int count)
    {
        await _ctx.Sut!.UpdateQueryCountAsync("SELECT [System.Id] FROM WorkItems", count, CancellationToken.None);
        _ctx.QueryCounts["SELECT [System.Id] FROM WorkItems"] = count;
    }

    [Then(@"the platform stores (\d+) as the cached count for that query")]
    public async Task ThenThePlatformStoresAsTheCachedCountForThatQuery(int expected)
    {
        var count = await _ctx.Sut!.GetQueryCountAsync("SELECT [System.Id] FROM WorkItems", CancellationToken.None);
        Assert.IsNotNull(count);
        Assert.AreEqual(expected, count.Value);
    }

    [Then("a subsequent check for the same query returns {int} without querying the source again")]
    public async Task ThenASubsequentCheckForTheSameQueryReturnsWithoutQueryingSourceAgain(int expected)
    {
        var count = await _ctx.Sut!.GetQueryCountAsync("SELECT [System.Id] FROM WorkItems", CancellationToken.None);
        Assert.AreEqual(expected, count);
    }

    // ── Scenario 8 ───────────────────────────────────────────────────────────

    [Given(@"the platform has cached a count of (\d+) for a WIQL query")]
    public async Task GivenThePlatformHasCachedACountForAWiqlQuery(int count)
    {
        await _ctx.Sut!.UpdateQueryCountAsync("SELECT [System.Id] FROM WorkItems", count, CancellationToken.None);
    }

    [When(@"the export records a new count of (\d+) for the same query")]
    public async Task WhenTheExportRecordsANewCountForTheSameQuery(int count)
    {
        await _ctx.Sut!.UpdateQueryCountAsync("SELECT [System.Id] FROM WorkItems", count, CancellationToken.None);
    }

    [Then(@"the platform returns (\d+) as the cached count for that query")]
    public async Task ThenThePlatformReturnsAsTheCachedCountForThatQuery(int expected)
    {
        var count = await _ctx.Sut!.GetQueryCountAsync("SELECT [System.Id] FROM WorkItems", CancellationToken.None);
        Assert.AreEqual(expected, count);
    }

    // ── Scenario 10 (restart) ─────────────────────────────────────────────────

    [When("the migration platform is restarted")]
    public void WhenTheMigrationPlatformIsRestarted()
    {
        // Re-open the same directory with a fresh store instance — simulates process restart.
        _ctx.Sut = new FileSystemWorkItemWatermarkStore(new FileSystemStateStore(_ctx.StoreDirectory!));
        _ctx.Restarted = true;
    }

    [Then(@"the platform still considers revision (\d+) of work item (\d+) as already processed")]
    public async Task ThenThePlatformStillConsidersRevisionOfWorkItemAsAlreadyProcessed(int revision, int workItemId)
    {
        var processed = await _ctx.Sut!.IsRevisionProcessedAsync(workItemId, revision, CancellationToken.None);
        Assert.IsTrue(processed, $"After restart: revision {revision} of work item {workItemId} should still be processed.");
    }

    [Then(@"the platform still considers revision (\d+) of work item (\d+) as not yet processed")]
    public async Task ThenThePlatformStillConsidersRevisionOfWorkItemAsNotYetProcessed(int revision, int workItemId)
    {
        var processed = await _ctx.Sut!.IsRevisionProcessedAsync(workItemId, revision, CancellationToken.None);
        Assert.IsFalse(processed, $"After restart: revision {revision} of work item {workItemId} should not be processed.");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_ctx.StoreDirectory != null && Directory.Exists(_ctx.StoreDirectory))
            Directory.Delete(_ctx.StoreDirectory, recursive: true);
    }
}
