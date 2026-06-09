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

    private static JobProgressStore CreateStore(int capacity = TestCapacity)
    {
        var opts = new Mock<IOptions<JobProgressOptions>>(MockBehavior.Strict);
        opts.Setup(o => o.Value).Returns(new JobProgressOptions { Capacity = capacity });
        return new JobProgressStore(opts.Object);
    }

    private static ProgressEvent MakeEvent(string stage) => new() { Module = "Test", Stage = stage };

    // ── Scenario: Ring buffer at capacity evicts oldest event ─────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RingBuffer_AtCapacity_EvictsOldestAndStoresNew()
    {
        var store = CreateStore();
        for (var i = 0; i < TestCapacity; i++)
            store.Append(s_jobId, MakeEvent($"Stage{i}"));

        var newEvent = MakeEvent("NewStage");
        store.Append(s_jobId, newEvent);

        var snapshot = store.GetSnapshot(s_jobId);
        Assert.AreEqual(TestCapacity, snapshot.Count, "Ring buffer should still hold exactly capacity events.");
        Assert.IsFalse(snapshot.Any(e => e.Stage == "Stage0"), "Oldest event (Stage0) should have been evicted.");
        Assert.IsTrue(snapshot.Any(e => e.Stage == "NewStage"), "Newest event should be present.");
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
