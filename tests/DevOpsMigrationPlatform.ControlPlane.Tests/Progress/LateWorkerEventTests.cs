// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.ControlPlane.Controllers;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

/// <summary>
/// PR #222 review: once the Control Plane has failed a job (e.g. its agent went silent), a late
/// worker event from the abandoned lease must not change it: a late Terminal must not replace the
/// failure with success, and a late Progress event must not reach the job's stream.
/// </summary>
[TestClass]
public sealed class LateWorkerEventTests
{
    private const string LeaseId = "lease-late";
    private const string WorkerId = "worker-late";
    private static readonly JsonSerializerOptions s_options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static (WorkerEventsController Controller, JobStore Jobs, JobProgressStore Progress, Guid JobId) FailedJob()
    {
        var jobId = Guid.NewGuid();
        var resolver = new Mock<ILeaseJobResolver>();
        resolver.Setup(r => r.ResolveJobId(LeaseId)).Returns(jobId);
        var progress = new JobProgressStore(Options.Create(new JobProgressOptions()));
        var jobs = new JobStore();
        jobs.Enqueue(new DevOpsMigrationPlatform.Abstractions.Jobs.Job { JobId = jobId.ToString(), Kind = DevOpsMigrationPlatform.Abstractions.Jobs.JobKind.Export });
        jobs.SetState(jobId, "Running");
        // The Control Plane fails it, as the silent-lease monitor does.
        jobs.SetState(jobId, "Failed");
        progress.CompleteJob(jobId, failed: true, failureReason: "agent went silent");
        var controller = new WorkerEventsController(resolver.Object, progress,
            new DiagnosticLogStore(Options.Create(new DiagnosticLogStoreOptions())), new JobMetricsStore(),
            new JobSnapshotStore(), new InMemoryJobTaskStore(), jobs, NullLogger<WorkerEventsController>.Instance);
        return (controller, jobs, progress, jobId);
    }

    private static void Post(WorkerEventsController controller, WorkerEventKind kind, object payload) =>
        controller.PostEvents(WorkerId, new WorkerEventBatch(WorkerId, LeaseId,
            new[] { new WorkerEvent(1, DateTimeOffset.UtcNow, kind, JsonSerializer.Serialize(payload, s_options)) }));

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void LateTerminalWithoutFailure_DoesNotReplaceTheFailure()
    {
        var (controller, jobs, progress, jobId) = FailedJob();

        Post(controller, WorkerEventKind.Terminal, new { failed = false });

        Assert.AreEqual("Failed", jobs.GetAllRecords().Single(r => r.Job.JobId == jobId.ToString()).State);
        Assert.IsTrue(progress.WasFailed(jobId), "The stream must keep the failure.");
        Assert.AreEqual("agent went silent", progress.GetFailureReason(jobId));
    }

    // PR #222 review: a write racing the silent-lease failure must land wholly before it or not
    // at all. A terminal transition waits while a write holds the job active.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task RunIfActive_TerminalTransitionWaitsUntilTheWriteIsDone()
    {
        var jobs = new JobStore();
        var jobId = Guid.NewGuid();
        jobs.Enqueue(new DevOpsMigrationPlatform.Abstractions.Jobs.Job { JobId = jobId.ToString(), Kind = DevOpsMigrationPlatform.Abstractions.Jobs.JobKind.Export });
        jobs.SetState(jobId, "Running");
        Task<bool>? failing = null;
        var failedDuringWrite = true;

        var ran = jobs.RunIfActive(jobId, () =>
        {
            failing = Task.Run(() => jobs.TrySetState(jobId, "Failed"));
            failedDuringWrite = failing.Wait(TimeSpan.FromMilliseconds(200));
        });

        Assert.IsTrue(ran);
        Assert.IsFalse(failedDuringWrite, "The failure must not land while the write holds the job active.");
        Assert.IsTrue(await failing!, "The failure lands once the write is done.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RunIfActive_FinishedJob_DoesNotRunTheWrite()
    {
        var (_, jobs, _, jobId) = FailedJob();
        var wrote = false;

        Assert.IsFalse(jobs.RunIfActive(jobId, () => wrote = true));
        Assert.IsFalse(wrote);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void DiagnosticAfterATerminalInTheSameBatch_IsNotStored()
    {
        var jobId = Guid.NewGuid();
        var resolver = new Mock<ILeaseJobResolver>();
        resolver.Setup(r => r.ResolveJobId(LeaseId)).Returns(jobId);
        var jobs = new JobStore();
        jobs.Enqueue(new DevOpsMigrationPlatform.Abstractions.Jobs.Job { JobId = jobId.ToString(), Kind = DevOpsMigrationPlatform.Abstractions.Jobs.JobKind.Export });
        jobs.SetState(jobId, "Running");
        var diagnostics = new DiagnosticLogStore(Options.Create(new DiagnosticLogStoreOptions()));
        var controller = new WorkerEventsController(resolver.Object, new JobProgressStore(Options.Create(new JobProgressOptions())),
            diagnostics, new JobMetricsStore(), new JobSnapshotStore(), new InMemoryJobTaskStore(), jobs, NullLogger<WorkerEventsController>.Instance);
        var record = new DiagnosticLogRecord { Level = "Error", Message = "after the end", Timestamp = DateTimeOffset.UtcNow };

        controller.PostEvents(WorkerId, new WorkerEventBatch(WorkerId, LeaseId, new[]
        {
            new WorkerEvent(1, DateTimeOffset.UtcNow, WorkerEventKind.Terminal, JsonSerializer.Serialize(new { failed = false }, s_options)),
            new WorkerEvent(2, DateTimeOffset.UtcNow, WorkerEventKind.Diagnostic, JsonSerializer.Serialize(new[] { record }, s_options))
        }));

        Assert.AreEqual(0, diagnostics.GetSnapshot(jobId).Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void LateProgress_IsNotAppendedToAFinishedJob()
    {
        var (controller, _, progress, jobId) = FailedJob();

        Post(controller, WorkerEventKind.Progress, new ProgressEvent { Module = "WorkItems", Stage = "Export", Message = "late" });

        Assert.IsFalse(progress.GetSnapshot(jobId).Any(e => e.Message == "late"));
    }
}
