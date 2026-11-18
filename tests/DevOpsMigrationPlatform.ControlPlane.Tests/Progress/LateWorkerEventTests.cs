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
