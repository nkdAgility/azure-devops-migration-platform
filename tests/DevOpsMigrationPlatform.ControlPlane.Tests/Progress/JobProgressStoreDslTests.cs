// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

[TestClass]
public class JobProgressStoreDslTests
{
    private const int TestCapacity = 3;
    private static readonly Guid s_jobId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_lateJobId = new("22222222-2222-2222-2222-222222222222");

    private static JobProgressStore CreateStore(int maxEventsPerJob = TestCapacity)
    {
        var opts = new Mock<IOptions<JobProgressOptions>>(MockBehavior.Strict);
        opts.Setup(o => o.Value).Returns(new JobProgressOptions { MaxEventsPerJob = maxEventsPerJob });
        return new JobProgressStore(opts.Object);
    }

    private static ProgressEvent MakeEvent(string stage) => new() { Module = "Test", Stage = stage };

    // ── Scenario: Append-only log at safety cap discards further events ────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AppendOnlyLog_AtSafetyCap_RetainsExistingAndDiscardsNew()
    {
        var store = CreateStore();
        for (var i = 0; i < TestCapacity; i++)
            store.Append(s_jobId, MakeEvent($"Stage{i}"));

        store.Append(s_jobId, MakeEvent("NewStage"));

        var snapshot = store.GetSnapshot(s_jobId);
        Assert.AreEqual(TestCapacity, snapshot.Count, "Log should hold exactly the cap; overflow is discarded, never evicted.");
        Assert.IsTrue(snapshot.Any(e => e.Stage == "Stage0"), "Earliest event must be retained — the log is append-only.");
        Assert.IsFalse(snapshot.Any(e => e.Stage == "NewStage"), "Event past the safety cap should have been discarded.");
    }

    // ── Scenario: CompleteJob before any Append marks channel completed ────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CompleteJobBeforeAppend_LateSubscriber_ChannelIsImmediatelyCompleted()
    {
        var store = CreateStore();
        store.CompleteJob(s_lateJobId);

        var (reader, _) = store.Subscribe(s_lateJobId);

        Assert.IsTrue(reader.Completion.IsCompleted,
            "Channel reader should be completed immediately after CompleteJob was called before subscription.");
        var snapshot = store.GetSnapshot(s_lateJobId);
        Assert.AreEqual(0, snapshot.Count, "No events should be buffered for a job completed before Append.");
    }
}
