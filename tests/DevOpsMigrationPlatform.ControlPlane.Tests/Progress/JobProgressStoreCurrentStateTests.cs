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

    private static ProgressEvent ProjectEvent(string module, string stage, string? message = null)
        => new ProgressEvent
        {
            Module = module,
            Stage = stage,
            Message = message,
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

        store.Append(jobId, ProjectEvent("Dependencies", "Analysis", "https://dev.azure.com/org|ProjectA"));
        store.Append(jobId, ProjectEvent("Dependencies", "ProjectComplete", "https://dev.azure.com/org|ProjectA"));

        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(1, state.Count, "Should have exactly one entry per module.");
        Assert.AreEqual("ProjectComplete", state[0].Stage);
    }

    [TestMethod]
    public void GetCurrentProjectState_MultipleProjects_ReturnsOneEntryPerModule()
    {
        var store = MakeStore();
        var jobId = Guid.NewGuid();

        store.Append(jobId, ProjectEvent("Dependencies", "ProjectComplete", "https://dev.azure.com/org|ProjectA"));
        store.Append(jobId, ProjectEvent("Inventory", "ProjectComplete", "https://dev.azure.com/org|ProjectB"));
        store.Append(jobId, ProjectEvent("WorkItems", "Analysis", "https://dev.azure.com/org|ProjectC"));

        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(3, state.Count, "Should have one entry per distinct Module.");
    }

    // ── Events without LastProcessed are excluded ─────────────────────────────

    [TestMethod]
    public void GetCurrentProjectState_EventsWithoutModule_AreExcluded()
    {
        var store = MakeStore();
        var jobId = Guid.NewGuid();

        // Event without Module (empty string — default)
        store.Append(jobId, new ProgressEvent { Module = "", Stage = "InventoryLoaded" });
        store.Append(jobId, ProjectEvent("Dependencies", "ProjectComplete", "https://dev.azure.com/org|ProjectA"));

        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(1, state.Count,
            "Events without Module should not appear in the current project state.");
        Assert.AreEqual("Dependencies", state[0].Module);
    }

    // ── Survives ring buffer eviction ─────────────────────────────────────────

    [TestMethod]
    public void GetCurrentProjectState_SurvivesRingBufferEviction()
    {
        // Ring buffer capacity of 2 — first event will be evicted from ring buffer
        var store = MakeStore(capacity: 2);
        var jobId = Guid.NewGuid();

        store.Append(jobId, ProjectEvent("Dependencies", "ProjectComplete", "https://dev.azure.com/org|ProjectA"));
        // Fill buffer past capacity — Dependencies event evicted from ring buffer
        store.Append(jobId, ProjectEvent("Inventory", "Analysis", "https://dev.azure.com/org|ProjectB"));
        store.Append(jobId, ProjectEvent("Inventory", "ProjectComplete", "https://dev.azure.com/org|ProjectB"));

        // Ring buffer only has last 2 events
        var snapshot = store.GetSnapshot(jobId);
        Assert.AreEqual(2, snapshot.Count);

        // But current project state should still have Dependencies entry
        var state = store.GetCurrentProjectState(jobId);
        Assert.AreEqual(2, state.Count,
            "GetCurrentProjectState should retain Dependencies even after ring buffer eviction.");
        Assert.IsTrue(state.Any(e => e.Module == "Dependencies"),
            "Dependencies should be present in the current project state despite ring buffer eviction.");
    }
}
