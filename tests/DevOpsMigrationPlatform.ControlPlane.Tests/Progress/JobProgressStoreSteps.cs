using DevOpsMigrationPlatform.ControlPlane.Tests.Progress;
using Reqnroll;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

[Binding]
internal sealed class JobProgressStoreSteps
{
    private readonly JobProgressStoreContext _ctx;

    public JobProgressStoreSteps(JobProgressStoreContext ctx) => _ctx = ctx;

    [Given(@"a JobProgressStore with a capacity of (\d+)")]
    public void GivenAJobProgressStoreWithCapacity(int capacity)
    {
        // Capacity is set in the context constructor (TestCapacity = 3); verified here.
        Assert.AreEqual(JobProgressStoreContext.TestCapacity, capacity,
            $"TestCapacity must match the scenario value. Expected {capacity}.");
    }

    [Given(@"the ring buffer for job ""([^""]+)"" is full with (\d+) events")]
    public void GivenTheRingBufferIsFull(string jobIdString, int count)
    {
        var jobId = Guid.Parse(jobIdString);
        for (var i = 0; i < count; i++)
            _ctx.Store.Append(jobId, _ctx.MakeEvent($"Stage{i}"));

        var snapshot = _ctx.Store.GetSnapshot(jobId);
        Assert.AreEqual(count, snapshot.Count,
            $"Ring buffer should contain {count} events before capacity test.");
    }

    [When(@"a new ProgressEvent is appended for that job")]
    public void WhenANewProgressEventIsAppended()
    {
        _ctx.LastAppendedEvent = _ctx.MakeEvent("NewStage");
        _ctx.Store.Append(_ctx.JobId, _ctx.LastAppendedEvent);
    }

    [Then("the oldest event is evicted")]
    public void ThenTheOldestEventIsEvicted()
    {
        var snapshot = _ctx.Store.GetSnapshot(_ctx.JobId);
        var hasStage0 = snapshot.Any(e => e.Stage == "Stage0");
        Assert.IsFalse(hasStage0, "Stage0 (oldest event) should have been evicted.");
    }

    [Then(@"the ring buffer contains exactly (\d+) events")]
    public void ThenTheRingBufferContainsExactly(int count)
    {
        var snapshot = _ctx.Store.GetSnapshot(_ctx.JobId);
        Assert.AreEqual(count, snapshot.Count,
            $"Ring buffer should contain exactly {count} events.");
    }

    [Then("the newest event is present in the snapshot")]
    public void ThenTheNewestEventIsPresentInTheSnapshot()
    {
        Assert.IsNotNull(_ctx.LastAppendedEvent,
            "LastAppendedEvent must be set by the When step.");
        var snapshot = _ctx.Store.GetSnapshot(_ctx.JobId);
        Assert.IsTrue(snapshot.Any(e => e.Stage == _ctx.LastAppendedEvent.Stage),
            $"Snapshot should contain the newest event with stage '{_ctx.LastAppendedEvent.Stage}'.");
    }
}
