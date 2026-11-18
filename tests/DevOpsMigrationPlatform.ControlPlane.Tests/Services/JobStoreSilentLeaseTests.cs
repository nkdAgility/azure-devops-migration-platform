// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Services;

/// <summary>
/// #171 live run (Martin, 2026-11-18): a leased job whose agent goes silent — no heartbeat and
/// no worker event within the timeout — is failed with a reason, so its dependents end too and a
/// re-run resumes it, instead of the job staying leased for ever.
/// </summary>
[TestClass]
public class JobStoreSilentLeaseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private static readonly ConnectorType[] TfsAgent = [ConnectorType.TeamFoundationServer];

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Job TfsExport() => new()
    {
        JobId = Guid.NewGuid().ToString(),
        Kind = JobKind.Export,
        Connectors = [ConnectorType.TeamFoundationServer]
    };

    private static string StateOf(JobStore store, Job job) =>
        store.GetAllRecords().Single(r => r.Job.JobId == job.JobId).State;

    private static async Task<JobStore> StoreWithLeasedJob(ManualTime time, Job job, Job? enqueueFirst = null, Action<JobStore>? beforeEnqueue = null)
    {
        var store = new JobStore(timeProvider: time);
        beforeEnqueue?.Invoke(store);
        if (enqueueFirst is not null)
            store.Enqueue(enqueueFirst);
        store.Enqueue(job);
        var leased = await store.DequeueAsync(TimeSpan.FromMilliseconds(20), TfsAgent, CancellationToken.None);
        Assert.AreEqual(job.JobId, leased?.JobId, "Arrange: the agent must lease the job.");
        store.SetState(Guid.Parse(job.JobId), "Running");
        return store;
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailSilentLeases_AgentSilentPastTheTimeout_FailsTheJobWithAReason()
    {
        var time = new ManualTime();
        var job = TfsExport();
        var store = await StoreWithLeasedJob(time, job);

        time.Now += Timeout + TimeSpan.FromSeconds(1);
        var failed = store.FailSilentLeases(Timeout);

        Assert.AreEqual("Failed", StateOf(store, job));
        var reason = failed.Single(f => f.JobId == Guid.Parse(job.JobId)).Reason;
        StringAssert.Contains(reason, "TfsMigrationAgent");
        StringAssert.Contains(reason, "again");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailSilentLeases_AgentStillSendingHeartbeats_LeavesTheJobRunning()
    {
        var time = new ManualTime();
        var job = TfsExport();
        var store = await StoreWithLeasedJob(time, job);

        time.Now += Timeout + Timeout;
        store.RecordLeaseActivity(Guid.Parse(job.JobId));
        var failed = store.FailSilentLeases(Timeout);

        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Running", StateOf(store, job));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailSilentLeases_FinishedJob_IsNotFailed()
    {
        var time = new ManualTime();
        var job = TfsExport();
        var store = await StoreWithLeasedJob(time, job);
        store.SetState(Guid.Parse(job.JobId), "Completed");

        time.Now += Timeout + Timeout;
        var failed = store.FailSilentLeases(Timeout);

        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Completed", StateOf(store, job));
    }

    // PR #222 review: a job in Completed or Failed must not change state again. A silent-lease
    // failure must not overwrite a completion that landed during the scan, and a late worker
    // event from the abandoned lease must not revive a failed job.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    [DataRow("Completed", "Failed")]
    [DataRow("Failed", "Completed")]
    [DataRow("Failed", "Running")]
    [DataRow("Completed", "Running")]
    public async Task SetState_FinishedJob_KeepsItsFinalState(string finalState, string lateState)
    {
        var time = new ManualTime();
        var job = TfsExport();
        var store = await StoreWithLeasedJob(time, job);
        store.SetState(Guid.Parse(job.JobId), finalState);

        store.SetState(Guid.Parse(job.JobId), lateState);

        Assert.AreEqual(finalState, StateOf(store, job));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailSilentLeases_JobCompletedDuringTheScan_StaysCompletedAndRaisesNothing()
    {
        var time = new TriggerTime();
        var job = TfsExport();
        var store = new JobStore(timeProvider: time);
        store.Enqueue(job);
        await store.DequeueAsync(TimeSpan.FromMilliseconds(20), TfsAgent, CancellationToken.None);
        store.SetState(Guid.Parse(job.JobId), "Running");
        var raised = new List<Guid>();
        store.JobFailedWithoutRunning += (id, _) => raised.Add(id);
        time.Now += Timeout + TimeSpan.FromSeconds(1);
        // The agent's completion lands on the clock read that starts the scan.
        time.OnNextRead = () => store.SetState(Guid.Parse(job.JobId), "Completed");

        var failed = store.FailSilentLeases(Timeout);

        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Completed", StateOf(store, job));
        Assert.AreEqual(0, raised.Count);
    }

    private sealed class TriggerTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        public Action? OnNextRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            var trigger = OnNextRead;
            OnNextRead = null;
            trigger?.Invoke();
            return Now;
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailSilentLeases_JobWithDependents_FailsThemAndRaisesEachReason()
    {
        var time = new ManualTime();
        var export = TfsExport();
        var import = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Import,
            Connectors = [ConnectorType.AzureDevOps],
            DependsOnJobId = export.JobId
        };
        var raised = new Dictionary<Guid, string>();
        // The import is held behind the export; the helper asserts the export was leased.
        var store = await StoreWithLeasedJob(time, export, enqueueFirst: import,
            beforeEnqueue: s => s.JobFailedWithoutRunning += (id, reason) => raised[id] = reason);

        time.Now += Timeout + TimeSpan.FromSeconds(1);
        store.FailSilentLeases(Timeout);

        Assert.AreEqual("Failed", StateOf(store, import));
        CollectionAssert.AreEquivalent(new[] { Guid.Parse(export.JobId), Guid.Parse(import.JobId) }, raised.Keys.ToArray());
        // PR #222 review: the reason the monitor publishes, not only the returned tuple.
        StringAssert.Contains(raised[Guid.Parse(export.JobId)], "sent no heartbeat");
    }
}
