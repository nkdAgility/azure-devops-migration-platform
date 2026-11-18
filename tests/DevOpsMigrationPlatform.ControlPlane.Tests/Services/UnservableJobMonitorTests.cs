// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Services;

/// <summary>
/// #169: when the Control Plane fails a job before any agent ran it, the reason must reach a
/// CLI that is following the job, and the job's stream must end as failed instead of hanging.
/// </summary>
[TestClass]
public class UnservableJobMonitorTests
{
    private static (JobStore Store, JobProgressStore Progress, DiagnosticLogStore Diagnostics, UnservableJobMonitor Monitor) Create()
    {
        var store = new JobStore();
        var progress = new JobProgressStore(Options.Create(new JobProgressOptions()));
        var diagnostics = new DiagnosticLogStore(Options.Create(new DiagnosticLogStoreOptions()));
        var monitor = new UnservableJobMonitor(store, progress, diagnostics, Options.Create(new JobRoutingOptions()));
        return (store, progress, diagnostics, monitor);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void JobFailedWithoutRunning_PublishesReasonAndEndsStreamAsFailed()
    {
        var (store, progress, diagnostics, monitor) = Create();
        using var _ = monitor;
        var export = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Export, Connectors = [ConnectorType.TeamFoundationServer] };
        var import = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Import, Connectors = [ConnectorType.AzureDevOps], DependsOnJobId = export.JobId };
        store.Enqueue(export);
        store.Enqueue(import);

        store.SetState(Guid.Parse(export.JobId), "Failed");

        var importId = Guid.Parse(import.JobId);
        var evt = progress.GetSnapshot(importId).Single();
        StringAssert.Contains(evt.Message, export.JobId);
        Assert.AreEqual("ControlPlane", evt.Module);
        Assert.IsTrue(evt.EventSequence > 0,
            "The event needs a sequence number, or the stream re-sends it when it replays after close.");
        Assert.IsTrue(progress.WasFailed(importId), "The job's progress stream must end as failed.");
        StringAssert.Contains(progress.GetFailureReason(importId), export.JobId,
            "The stream's job-failed event carries this reason to the CLI.");
        Assert.IsTrue(diagnostics.WasFailed(importId));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void JobRoutingOptions_DefaultUnservableTimeout_IsLongerThanOneLeasePoll()
    {
        // Agents long-poll for 30 s; a shorter timeout could fail a job between two polls.
        Assert.IsTrue(new JobRoutingOptions().UnservableJobTimeout > TimeSpan.FromSeconds(30));
    }
}
