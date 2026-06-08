// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Jobs;

/// <summary>
/// DSL-style tests for the job submission feature.
/// Covers: submitting export/import/migrate jobs and dequeuing a submitted job.
/// </summary>
[TestClass]
public sealed class JobSubmissionDslTests
{
    // ── Scenario: Submit an export job ────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public void Enqueue_ExportJob_IsInQueuedState()
    {
        // Arrange – a running control plane with a job store
        var store = new JobStore();

        // Act – operator submits an export job
        var job = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Export };
        var jobId = store.Enqueue(job);

        // Assert – job should be in Queued state
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Queued", record.State, "Submitted export job should be in Queued state");

        // Assert – job has a unique job ID
        Assert.IsFalse(string.IsNullOrEmpty(job.JobId), "Export job should have a unique job ID");
        Assert.AreEqual(jobId, Guid.Parse(job.JobId), "Job ID returned from Enqueue should match the submitted job's ID");
    }

    // ── Scenario: Submit an import job ────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public void Enqueue_ImportJob_IsInQueuedState()
    {
        // Arrange – a running control plane with a job store
        var store = new JobStore();

        // Act – operator submits an import job
        var job = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Import };
        var jobId = store.Enqueue(job);

        // Assert – job should be in Queued state
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Queued", record.State, "Submitted import job should be in Queued state");

        // Assert – job has a unique job ID
        Assert.IsFalse(string.IsNullOrEmpty(job.JobId), "Import job should have a unique job ID");
        Assert.AreEqual(jobId, Guid.Parse(job.JobId), "Job ID returned from Enqueue should match the submitted job's ID");
    }

    // ── Scenario: Submit a both-mode job ─────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public void Enqueue_MigrateJob_IsInQueuedState()
    {
        // Arrange – a running control plane with a job store
        var store = new JobStore();

        // Act – operator submits a both-mode (Migrate) job with source and target
        var job = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Migrate };
        var jobId = store.Enqueue(job);

        // Assert – job should be in Queued state
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Queued", record.State, "Submitted both-mode (Migrate) job should be in Queued state");
    }

    // ── Scenario: Dequeue a submitted job ─────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task DequeueAsync_AfterSubmittingExportJob_ReturnsMatchingJob()
    {
        // Arrange – operator has submitted an export job
        var store = new JobStore();
        var job = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Export };
        store.Enqueue(job);

        // Act – migration agent dequeues the next job
        var dequeued = await store.DequeueAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert – dequeued job should match the submitted job
        Assert.IsNotNull(dequeued, "Agent should successfully dequeue the submitted job");
        Assert.AreEqual(job.JobId, dequeued.JobId, "Dequeued job ID should match the submitted job's ID");
        Assert.AreEqual(JobKind.Export, dequeued.Kind, "Dequeued job kind should match the submitted export job");
    }
}
