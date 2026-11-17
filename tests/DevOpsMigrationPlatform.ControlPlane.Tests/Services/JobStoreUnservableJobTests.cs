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
/// #169 / ADR-0035: a queued job whose connectors no polling agent advertises fails fast with
/// an actionable message instead of re-queuing forever. "Connected" means an agent asked for a
/// lease, advertising its connectors, within the unservable-job timeout.
/// </summary>
[TestClass]
public class JobStoreUnservableJobTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private static readonly ConnectorType[] AdoAgent = [ConnectorType.AzureDevOps, ConnectorType.Simulated];
    private static readonly ConnectorType[] TfsAgent = [ConnectorType.TeamFoundationServer];

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
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

    private static Task Poll(JobStore store, ConnectorType[] agent) =>
        store.DequeueAsync(TimeSpan.FromMilliseconds(20), agent, CancellationToken.None);

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_NoAgentAdvertisesTheConnector_FailsWithActionableMessage()
    {
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var job = TfsExport();
        store.Enqueue(job);
        await Poll(store, AdoAgent);

        time.Now += Timeout + TimeSpan.FromSeconds(1);
        await Poll(store, AdoAgent);
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual("Failed", StateOf(store, job));
        var reason = failed.Single(f => f.JobId == Guid.Parse(job.JobId)).Reason;
        StringAssert.Contains(reason, "TeamFoundationServer");
        StringAssert.Contains(reason, "TfsMigrationAgent");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_BeforeTimeout_LeavesJobQueued()
    {
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var job = TfsExport();
        store.Enqueue(job);
        await Poll(store, AdoAgent);

        time.Now += Timeout - TimeSpan.FromSeconds(1);
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Queued", StateOf(store, job));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_CapableAgentPolledWithinTimeout_LeavesJobQueued()
    {
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var job = TfsExport();
        store.Enqueue(job);

        time.Now += Timeout + TimeSpan.FromSeconds(1);
        // A TFS agent asked for work recently but did not take the job (e.g. it was busy elsewhere
        // and the job was re-queued); the job is servable and must keep waiting.
        store.RecordAgentPoll(TfsAgent);
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Queued", StateOf(store, job));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_CapableAgentBusyOnALongJob_LeavesJobQueued()
    {
        // PR #222 review: an agent runs a leased job to completion before it polls again, so a
        // long migration must not make its agent look disconnected and fail the next job.
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var running = TfsExport();
        store.Enqueue(running);
        await Poll(store, TfsAgent);
        store.SetState(Guid.Parse(running.JobId), "Running");
        var next = TfsExport();
        store.Enqueue(next);

        time.Now += Timeout + Timeout;
        // The busy agent keeps sending worker events (heartbeats, progress) for its lease.
        store.RecordLeaseActivity(Guid.Parse(running.JobId));
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Queued", StateOf(store, next));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_AgentDisconnectedMidJob_FailsJob()
    {
        // PR #222 review: a lease counts only while its agent shows it is alive. An agent that
        // vanished mid-job without a terminal state must not keep later jobs queued for ever.
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var running = TfsExport();
        store.Enqueue(running);
        await Poll(store, TfsAgent);
        store.SetState(Guid.Parse(running.JobId), "Running");
        var next = TfsExport();
        store.Enqueue(next);

        time.Now += Timeout + Timeout;
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(1, failed.Count);
        Assert.AreEqual("Failed", StateOf(store, next));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_BusyAgentFinishedLongAgo_FailsJob()
    {
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var done = TfsExport();
        store.Enqueue(done);
        await Poll(store, TfsAgent);
        store.SetState(Guid.Parse(done.JobId), "Completed");
        var next = TfsExport();
        store.Enqueue(next);

        time.Now += Timeout + Timeout;
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(1, failed.Count, "A finished lease no longer shows its agent is connected.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FailUnservableJobs_AgentAdvertisingNoConnectors_DoesNotKeepAConnectorJobQueued()
    {
        // PR #222 review: an agent polling with an empty capability list can lease only
        // connector-less jobs, so it must not count as able to serve a TFS job.
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var job = TfsExport();
        store.Enqueue(job);

        time.Now += Timeout + TimeSpan.FromSeconds(1);
        await Poll(store, []);
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(1, failed.Count);
        Assert.AreEqual("Failed", StateOf(store, job));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void FailUnservableJobs_HeldDependentJob_IsNotFailedWhileWaiting()
    {
        var time = new ManualTime();
        var store = new JobStore(timeProvider: time);
        var export = TfsExport();
        var import = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Import,
            Connectors = [ConnectorType.AzureDevOps],
            DependsOnJobId = export.JobId
        };
        store.Enqueue(export);
        store.Enqueue(import);
        store.SetState(Guid.Parse(export.JobId), "Running");

        time.Now += Timeout + TimeSpan.FromSeconds(1);
        store.RecordAgentPoll(TfsAgent);
        var failed = store.FailUnservableJobs(Timeout);

        Assert.AreEqual(0, failed.Count, "A held job waits on its dependency, not on an agent.");
        Assert.AreEqual("Queued", StateOf(store, import));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void JobFailedWithoutRunning_IsRaisedWithReason_WhenDependencyFails()
    {
        var store = new JobStore();
        var export = TfsExport();
        var import = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Import,
            Connectors = [ConnectorType.AzureDevOps],
            DependsOnJobId = export.JobId
        };
        var raised = new List<(Guid JobId, string Reason)>();
        store.JobFailedWithoutRunning += (id, reason) => raised.Add((id, reason));
        store.Enqueue(export);
        store.Enqueue(import);

        store.SetState(Guid.Parse(export.JobId), "Failed");

        var (jobId, reason) = raised.Single();
        Assert.AreEqual(Guid.Parse(import.JobId), jobId);
        StringAssert.Contains(reason, export.JobId);
    }
}
