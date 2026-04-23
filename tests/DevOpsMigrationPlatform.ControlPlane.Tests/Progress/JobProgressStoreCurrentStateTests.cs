using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

[TestClass]
public class JobProgressStoreCurrentStateTests
{
    private static JobProgressStore MakeStore(int capacity = 100)
    {
        var options = new Mock<IOptions<JobProgressOptions>>(MockBehavior.Strict);
        options.Setup(o => o.Value).Returns(new JobProgressOptions { Capacity = capacity });
        return new JobProgressStore(options.Object);
    }

    private static ProgressEvent ProjectEvent(string projectKey, string stage, int analysed = 0)
        => new ProgressEvent
        {
            Module = "Dependencies",
            Stage = stage,
            LastProcessed = projectKey,
            WorkItemsProcessed = analysed,
            Timestamp = DateTimeOffset.UtcNow
        };

    // ── GetCurrentProjectState returns empty for unknown job ─────────────────

    [TestMethod]
    public void GetCurrentProjectState_UnknownJob_ReturnsEmpty()
    {
        var store = MakeStore();
        var result = store.GetCurrentProjectState(Guid.NewGuid());
        Assert.AreEqual(0, result.Count);
    }

    // ── Latest event per project key is tracked ──────────────────────────────

    [TestMethod]
    public void GetCurrentProjectState_SingleProject_ReturnsLatestEvent()
    {
        var store = MakeStore();
        var jobId = Guid.NewGuid();
        var projectKey = "https://dev.azure.com/org|ProjectA";

        store.Append(jobId, ProjectEvent(projectKey, "Analysis", analysed: 100));
        store.Append(jobId, ProjectEvent(projectKey, "ProjectComplete", analysed: 200));

        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(1, state.Count, "Should have exactly one entry per project key.");
        Assert.AreEqual("ProjectComplete", state[0].Stage);
        Assert.AreEqual(200, state[0].WorkItemsProcessed);
    }

    [TestMethod]
    public void GetCurrentProjectState_MultipleProjects_ReturnsOneEntryPerProject()
    {
        var store = MakeStore();
        var jobId = Guid.NewGuid();

        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectA", "ProjectComplete", 100));
        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectB", "ProjectComplete", 200));
        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectC", "Analysis", 50));

        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(3, state.Count, "Should have one entry per distinct LastProcessed key.");
    }

    // ── Events without LastProcessed are excluded ─────────────────────────────

    [TestMethod]
    public void GetCurrentProjectState_EventsWithoutLastProcessed_AreExcluded()
    {
        var store = MakeStore();
        var jobId = Guid.NewGuid();

        // Event without LastProcessed (e.g. InventoryLoaded or Completed stage)
        store.Append(jobId, new ProgressEvent { Module = "Dependencies", Stage = "InventoryLoaded" });
        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectA", "ProjectComplete", 100));

        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(1, state.Count,
            "Events without LastProcessed should not appear in the current project state.");
        Assert.AreEqual("https://dev.azure.com/org|ProjectA", state[0].LastProcessed);
    }

    // ── Survives ring buffer eviction ─────────────────────────────────────────

    [TestMethod]
    public void GetCurrentProjectState_SurvivesRingBufferEviction()
    {
        // Ring buffer capacity of 2 — project A's event will be evicted
        var store = MakeStore(capacity: 2);
        var jobId = Guid.NewGuid();

        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectA", "ProjectComplete", 100));
        // Fill buffer past capacity — ProjectA's event evicted from ring buffer
        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectB", "Analysis", 50));
        store.Append(jobId, ProjectEvent("https://dev.azure.com/org|ProjectB", "ProjectComplete", 200));

        // Ring buffer only has last 2 events (ProjectB Analysis + ProjectB Complete)
        var snapshot = store.GetSnapshot(jobId);
        Assert.AreEqual(2, snapshot.Count);
        Assert.IsFalse(snapshot.Any(e => e.LastProcessed == "https://dev.azure.com/org|ProjectA"),
            "ProjectA event should have been evicted from the ring buffer.");

        // But current project state should still have ProjectA
        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(2, state.Count,
            "GetCurrentProjectState should retain ProjectA even after ring buffer eviction.");
        Assert.IsTrue(state.Any(e => e.LastProcessed == "https://dev.azure.com/org|ProjectA"),
            "ProjectA should be present in the current project state despite ring buffer eviction.");
    }
}
