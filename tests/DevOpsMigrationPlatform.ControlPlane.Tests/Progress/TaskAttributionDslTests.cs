// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

/// <summary>
/// DSL tests for task attribution via ProgressEvent.TaskId / TaskStatus fields.
/// Covers feature: features/platform/task-attribution.feature
/// </summary>
[TestClass]
public sealed class TaskAttributionDslTests
{
    private static readonly Guid s_jobId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string LeaseId = "lease-task-attribution";

    private static (ProgressControllerContext ctx, Guid jobId) BuildContext()
    {
        var ctx = new ProgressControllerContext();
        ctx.LeaseResolver.Setup(r => r.ResolveJobId(LeaseId)).Returns(s_jobId);

        // Push execution plan containing "export.identities" and "export.workitems"
        var tasks = new List<JobTask>
        {
            new() { Id = "export.identities", Name = "export.identities", Order = 0, Status = JobTaskStatus.Pending },
            new() { Id = "export.workitems",  Name = "export.workitems",  Order = 1, Status = JobTaskStatus.Pending }
        };
        ctx.TaskStore.Store(s_jobId, new JobTaskList { Tasks = tasks.AsReadOnly() });

        return (ctx, s_jobId);
    }

    // ── Scenario: TaskStatus_WhenRunningEventReceived_TransitionsTaskToRunning ──

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TaskStatus_WhenRunningEventReceived_TransitionsTaskToRunning()
    {
        var (ctx, jobId) = BuildContext();

        ctx.Controller.PostProgress(LeaseId, new ProgressEvent
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
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TaskStatus_WhenCompletedEventReceived_TransitionsTaskToCompleted()
    {
        var (ctx, jobId) = BuildContext();
        var startTime = DateTimeOffset.UtcNow.AddSeconds(-5);
        var completeTime = DateTimeOffset.UtcNow;

        // Pre-apply Running event
        ctx.Controller.PostProgress(LeaseId, new ProgressEvent
        {
            Module = "Test",
            Stage = "Start",
            TaskId = "export.identities",
            TaskStatus = JobTaskStatus.Running,
            Timestamp = startTime
        });

        // Post Completed event
        ctx.Controller.PostProgress(LeaseId, new ProgressEvent
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
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TaskStatus_WhenFailedEventReceived_TransitionsTaskToFailed()
    {
        var (ctx, jobId) = BuildContext();
        var startTime = DateTimeOffset.UtcNow.AddSeconds(-5);

        // Pre-apply Running event
        ctx.Controller.PostProgress(LeaseId, new ProgressEvent
        {
            Module = "Test",
            Stage = "Start",
            TaskId = "export.workitems",
            TaskStatus = JobTaskStatus.Running,
            Timestamp = startTime
        });

        // Post Failed event
        ctx.Controller.PostProgress(LeaseId, new ProgressEvent
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
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TaskStatus_WhenEventHasNoTaskId_OtherTasksUnchanged()
    {
        var (ctx, jobId) = BuildContext();

        // Post event with no TaskId
        ctx.Controller.PostProgress(LeaseId, new ProgressEvent
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
