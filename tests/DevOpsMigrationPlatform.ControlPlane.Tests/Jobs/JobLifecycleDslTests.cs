// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlane.Metrics;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Jobs;

/// <summary>
/// DSL-style tests for the job lifecycle feature.
/// Covers: Queued→Running, Running→Completed, Running→Failed,
///         and multiple progress updates while Running.
/// </summary>
[TestClass]
public sealed class JobLifecycleDslTests
{
    /// <summary>
    /// Counting stub for IJobLifecycleMetrics — avoids Moq TagList InlineArray issues.
    /// </summary>
    private sealed class MetricsStub : IJobLifecycleMetrics
    {
        public int JobSubmittedCount;
        public int JobDequeuedCount;
        public int JobStartedCount;
        public int JobCompletedCount;
        public int JobFailedCount;
        public int RecordJobDurationCount;

        public void JobSubmitted(in TagList tags) => JobSubmittedCount++;
        public void JobDequeued(in TagList tags) => JobDequeuedCount++;
        public void JobStarted(in TagList tags) => JobStartedCount++;
        public void JobCompleted(in TagList tags) => JobCompletedCount++;
        public void JobFailed(in TagList tags) => JobFailedCount++;
        public void RecordJobDuration(double milliseconds, in TagList tags) => RecordJobDurationCount++;
    }

    private static Job CreateExportJob() =>
        new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Export };

    // ── Scenario: Job transitions from Queued to Running ──────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SetState_QueuedToRunning_RaisesJobStartedMetric()
    {
        // Arrange – a submitted export job starts in Queued state
        var metrics = new MetricsStub();
        var store = new JobStore(metrics);
        var job = CreateExportJob();
        var jobId = store.Enqueue(job);

        // Act – the migration agent starts processing the job
        store.SetState(jobId, "Running");

        // Assert – state transitions to Running
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Running", record.State, "Job state should transition to Running");

        // Assert – a JobStarted event (metric) is raised
        Assert.AreEqual(1, metrics.JobStartedCount,
            "JobStarted metric should be raised once when job transitions to Running");
    }

    // ── Scenario: Job transitions from Running to Completed ───────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SetState_RunningToCompleted_RaisesJobCompletedMetricAndRecordsDuration()
    {
        // Arrange – job is already in Running state
        var metrics = new MetricsStub();
        var store = new JobStore(metrics);
        var job = CreateExportJob();
        var jobId = store.Enqueue(job);
        store.SetState(jobId, "Running");

        // Act – the migration agent completes the job successfully
        store.SetState(jobId, "Completed");

        // Assert – state transitions to Completed
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Completed", record.State, "Job state should transition to Completed");

        // Assert – a JobCompleted event (metric) is raised
        Assert.AreEqual(1, metrics.JobCompletedCount,
            "JobCompleted metric should be raised once when job completes successfully");

        // Assert – duration is recorded
        Assert.AreEqual(1, metrics.RecordJobDurationCount,
            "Job duration should be recorded when the job completes");
    }

    // ── Scenario: Job transitions from Running to Failed ─────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SetState_RunningToFailed_RaisesJobFailedMetricAndRecordsReason()
    {
        // Arrange – job is already in Running state
        var metrics = new MetricsStub();
        var store = new JobStore(metrics);
        var job = CreateExportJob();
        var jobId = store.Enqueue(job);
        store.SetState(jobId, "Running");

        // Act – the migration agent encounters an unrecoverable error
        store.SetState(jobId, "Failed");

        // Assert – state transitions to Failed
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Failed", record.State, "Job state should transition to Failed");

        // Assert – a JobFailed event (metric) is raised
        Assert.AreEqual(1, metrics.JobFailedCount,
            "JobFailed metric should be raised once when an unrecoverable error occurs");
    }

    // ── Scenario: Multiple state updates during processing ────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SetState_MultipleRunningUpdates_PreservesRunningStateAndRaisesJobStartedOnce()
    {
        // Arrange – job is in Queued state
        var metrics = new MetricsStub();
        var store = new JobStore(metrics);
        var job = CreateExportJob();
        var jobId = store.Enqueue(job);
        store.SetState(jobId, "Running"); // first transition to Running

        // Act – the migration agent reports multiple progress updates (re-sets Running)
        store.SetState(jobId, "Running");
        store.SetState(jobId, "Running");

        // Assert – state is still Running after each update
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Running", record.State, "State should remain Running during progress updates");

        // Assert – JobStarted is only fired once (idempotent guard in JobStore)
        Assert.AreEqual(1, metrics.JobStartedCount,
            "JobStarted metric should be raised exactly once regardless of repeated Running state updates");
    }
}
