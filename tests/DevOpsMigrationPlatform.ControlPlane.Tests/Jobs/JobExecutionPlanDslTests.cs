// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.ControlPlane.Controllers;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Jobs;

[TestClass]
public sealed class JobExecutionPlanDslTests
{
    private static readonly Guid s_jobId = new("44444444-4444-4444-4444-444444444444");
    private const string LeaseId = "lease-44444444";

    private static TelemetryController BuildController(
        InMemoryJobTaskStore taskStore,
        Mock<ILeaseJobResolver>? resolver = null)
    {
        resolver ??= new Mock<ILeaseJobResolver>(MockBehavior.Strict);
        var telemetryStore = new JobMetricsStore();
        var snapshotStore = new JobSnapshotStore();
        var progressOptions = new Mock<Microsoft.Extensions.Options.IOptions<JobProgressOptions>>(MockBehavior.Strict);
        progressOptions.Setup(o => o.Value).Returns(new JobProgressOptions { Capacity = 5 });
        var progressStore = new JobProgressStore(progressOptions.Object);
        return new TelemetryController(telemetryStore, snapshotStore, progressStore, taskStore, resolver.Object);
    }

    private static JobTaskList MakeTaskList(int count)
    {
        var tasks = new List<JobTask>();
        for (int i = 0; i < count; i++)
            tasks.Add(new JobTask { Id = $"task.{i}", Name = $"Task {i}", Order = i, Status = JobTaskStatus.Pending });
        return new JobTaskList { Tasks = tasks.AsReadOnly() };
    }

    // ── Scenario: Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks ──

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks()
    {
        var taskStore = new InMemoryJobTaskStore();
        var resolver = new Mock<ILeaseJobResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveJobId(LeaseId)).Returns(s_jobId);

        var controller = BuildController(taskStore, resolver);

        // Agent pushes an execution plan with 4 tasks
        var taskList = MakeTaskList(4);
        controller.PushTasks(LeaseId, taskList);

        // Client calls GET /jobs/{jobId}/bootstrap
        var result = controller.GetBootstrap(s_jobId.ToString()) as OkObjectResult;
        Assert.IsNotNull(result, "Expected 200 OK from GetBootstrap");

        var bootstrap = result.Value as JobBootstrap;
        Assert.IsNotNull(bootstrap, "Bootstrap value should be a JobBootstrap");
        Assert.IsNotNull(bootstrap.Tasks, "Tasks should be non-null after agent pushed plan");
        Assert.AreEqual(4, bootstrap.Tasks.Tasks.Count, "Should contain 4 tasks");

        // Verify ascending order
        for (int i = 0; i < bootstrap.Tasks.Tasks.Count; i++)
            Assert.AreEqual(i, bootstrap.Tasks.Tasks[i].Order, $"Task at index {i} should have Order={i}");
    }

    // ── Scenario: Bootstrap_BeforePlanPushed_ReturnNullTasks ──────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Bootstrap_BeforePlanPushed_ReturnNullTasks()
    {
        var taskStore = new InMemoryJobTaskStore();
        var controller = BuildController(taskStore);

        // Agent has NOT pushed an execution plan
        var result = controller.GetBootstrap(s_jobId.ToString()) as OkObjectResult;
        Assert.IsNotNull(result, "Expected 200 OK from GetBootstrap");

        var bootstrap = result.Value as JobBootstrap;
        Assert.IsNotNull(bootstrap, "Bootstrap value should be a JobBootstrap");
        Assert.IsNull(bootstrap.Tasks, "Tasks should be null when no plan has been pushed");
    }

    // ── Scenario: GetTasks_WhenTaskListExists_ReturnsCurrentTaskList ──────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void GetTasks_WhenTaskListExists_ReturnsCurrentTaskList()
    {
        var taskStore = new InMemoryJobTaskStore();
        var resolver = new Mock<ILeaseJobResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveJobId(LeaseId)).Returns(s_jobId);

        var controller = BuildController(taskStore, resolver);
        controller.PushTasks(LeaseId, MakeTaskList(3));

        // Client calls GET /jobs/{jobId}/tasks
        var result = controller.GetTasks(s_jobId.ToString()) as OkObjectResult;
        Assert.IsNotNull(result, "Expected 200 OK from GetTasks");

        var list = result.Value as JobTaskList;
        Assert.IsNotNull(list, "Response value should be a JobTaskList");
        Assert.AreEqual(3, list.Tasks.Count, "Task list should contain 3 tasks");
    }

    // ── Scenario: GetTasks_WhenNoTaskListPushed_Returns204 ───────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void GetTasks_WhenNoTaskListPushed_Returns204()
    {
        var taskStore = new InMemoryJobTaskStore();
        var controller = BuildController(taskStore);

        // No execution plan pushed
        var result = controller.GetTasks(s_jobId.ToString());
        var statusCode = (result as StatusCodeResult)?.StatusCode
                      ?? (result as NoContentResult)?.StatusCode;
        Assert.AreEqual(204, statusCode, "Should return 204 when no task list has been pushed");
    }
}
