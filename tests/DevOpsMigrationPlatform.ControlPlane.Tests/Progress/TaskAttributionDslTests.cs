// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
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
/// DSL tests for task attribution via ProgressEvent.TaskId / TaskStatus fields,
/// delivered over the unified worker events channel.
/// Covers feature: features/platform/task-attribution.feature
/// </summary>
[TestClass]
public sealed class TaskAttributionDslTests
{
    private static readonly Guid s_jobId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string LeaseId = "lease-task-attribution";
    private const string WorkerId = "worker-task-attribution";

    private static readonly JsonSerializerOptions s_payloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record Context(WorkerEventsController Controller, InMemoryJobTaskStore TaskStore);

    private static (Context ctx, Guid jobId) BuildContext()
    {
        var resolver = new Mock<ILeaseJobResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveJobId(LeaseId)).Returns(s_jobId);

        var progressOptions = new Mock<IOptions<JobProgressOptions>>(MockBehavior.Strict);
        progressOptions.Setup(o => o.Value).Returns(new JobProgressOptions());
        var progressStore = new JobProgressStore(progressOptions.Object);

        var diagOptions = new Mock<IOptions<DiagnosticLogStoreOptions>>(MockBehavior.Strict);
        diagOptions.Setup(o => o.Value).Returns(new DiagnosticLogStoreOptions());
        var diagnosticStore = new DiagnosticLogStore(diagOptions.Object);

        var taskStore = new InMemoryJobTaskStore();

        var controller = new WorkerEventsController(
            resolver.Object,
            progressStore,
            diagnosticStore,
            new JobMetricsStore(),
            new JobSnapshotStore(),
            taskStore,
            new JobStore(),
            NullLogger<WorkerEventsController>.Instance);

        // Push execution plan containing "export.identities" and "export.workitems"
        var tasks = new List<JobTask>
        {
            new() { Id = "export.identities", Name = "export.identities", Order = 0, Status = JobTaskStatus.Pending },
            new() { Id = "export.workitems",  Name = "export.workitems",  Order = 1, Status = JobTaskStatus.Pending }
        };
        taskStore.Store(s_jobId, new JobTaskList { Tasks = tasks.AsReadOnly() });

        return (new Context(controller, taskStore), s_jobId);
    }

    private static long s_seq;

    private static void PostProgress(Context ctx, ProgressEvent evt)
    {
        var workerEvent = new WorkerEvent(
            ++s_seq,
            evt.Timestamp,
            WorkerEventKind.Progress,
            JsonSerializer.Serialize(evt, s_payloadOptions));

        ctx.Controller.PostEvents(WorkerId, new WorkerEventBatch(WorkerId, LeaseId, new[] { workerEvent }));
    }

    // ── Scenario: TaskStatus_WhenRunningEventReceived_TransitionsTaskToRunning ──

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TaskStatus_WhenRunningEventReceived_TransitionsTaskToRunning()
    {
        var (ctx, jobId) = BuildContext();

        PostProgress(ctx, new ProgressEvent
        {
            Module = "Test",
            Stage = "Start",
            TaskId = "export.identities",
            TaskStatus = JobTaskStatus.Running,
            Timestamp = DateTimeOffset.UtcNow
        });

        var list = ctx.TaskStore.GetLatest(jobId)!;
        var task = list.Tasks[0];
        Assert.AreEqual(JobTaskStatus.Running, task.Status,
            "Task export.identities should be Running after Running event");
    }

    // ── Scenario: TaskStatus_WhenCompletedEventReceived_TransitionsTaskToCompleted ──

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TaskStatus_WhenCompletedEventReceived_TransitionsTaskToCompleted()
    {
        var (ctx, jobId) = BuildContext();
        var startTime = DateTimeOffset.UtcNow.AddSeconds(-5);
        var completeTime = DateTimeOffset.UtcNow;

        // Pre-apply Running event
        PostProgress(ctx, new ProgressEvent
        {
            Module = "Test",
            Stage = "Start",
            TaskId = "export.identities",
            TaskStatus = JobTaskStatus.Running,
            Timestamp = startTime
        });

        // Post Completed event
        PostProgress(ctx, new ProgressEvent
        {
            Module = "Test",
            Stage = "Complete",
            TaskId = "export.identities",
            TaskStatus = JobTaskStatus.Completed,
            Timestamp = completeTime
        });

        var list = ctx.TaskStore.GetLatest(jobId)!;
        var task = list.Tasks[0];
        Assert.AreEqual(JobTaskStatus.Completed, task.Status,
            "Task export.identities should be Completed after Completed event");
        Assert.IsNotNull(task.CompletedAt,
            "Task export.identities CompletedAt should be set after Completed event");
    }

    // ── Scenario: TaskStatus_WhenFailedEventReceived_TransitionsTaskToFailed ──

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TaskStatus_WhenFailedEventReceived_TransitionsTaskToFailed()
    {
        var (ctx, jobId) = BuildContext();
        var startTime = DateTimeOffset.UtcNow.AddSeconds(-5);

        // Pre-apply Running event
        PostProgress(ctx, new ProgressEvent
        {
            Module = "Test",
            Stage = "Start",
            TaskId = "export.workitems",
            TaskStatus = JobTaskStatus.Running,
            Timestamp = startTime
        });

        // Post Failed event
        PostProgress(ctx, new ProgressEvent
        {
            Module = "Test",
            Stage = "Fail",
            TaskId = "export.workitems",
            TaskStatus = JobTaskStatus.Failed,
            Timestamp = DateTimeOffset.UtcNow
        });

        var list = ctx.TaskStore.GetLatest(jobId)!;
        var task = list.Tasks[1];
        Assert.AreEqual(JobTaskStatus.Failed, task.Status,
            "Task export.workitems should be Failed after Failed event");
    }

    // ── Scenario: TaskStatus_WhenEventHasNoTaskId_OtherTasksUnchanged ──

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TaskStatus_WhenEventHasNoTaskId_OtherTasksUnchanged()
    {
        var (ctx, jobId) = BuildContext();

        // Post event with no TaskId
        PostProgress(ctx, new ProgressEvent
        {
            Module = "Test",
            Stage = "SomeStage",
            TaskId = null,
            TaskStatus = null,
            Timestamp = DateTimeOffset.UtcNow
        });

        var list = ctx.TaskStore.GetLatest(jobId)!;
        foreach (var task in list.Tasks)
        {
            Assert.AreEqual(JobTaskStatus.Pending, task.Status,
                $"Task {task.Id} should remain Pending when event has no TaskId");
        }
    }
}
