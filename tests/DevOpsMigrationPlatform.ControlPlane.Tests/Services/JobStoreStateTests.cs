using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Services;

[TestClass]
[TestCategory("Unit")]
public class JobStoreStateTests
{
    private static Job CreateJob(string id) =>
        new Job
        {
            JobId = id,
            Kind = JobKind.Export
        };

    [TestMethod]
    public void Enqueue_SetsInitialStateToQueued()
    {
        // Arrange
        var store = new JobStore();
        var job = CreateJob(Guid.NewGuid().ToString());

        // Act
        var jobId = store.Enqueue(job);

        // Assert
        var record = store.GetAllRecords().Single(r => r.Job.JobId == job.JobId);
        Assert.AreEqual("Queued", record.State);
    }

    [TestMethod]
    public void SetState_Leased_TransitionsFromQueued()
    {
        // Arrange
        var store = new JobStore();
        var jobId = store.Enqueue(CreateJob(Guid.NewGuid().ToString()));

        // Act
        store.SetState(jobId, "Leased");

        // Assert
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Leased", record.State);
    }

    [TestMethod]
    public void SetState_Running_TransitionsFromLeased()
    {
        // Arrange
        var store = new JobStore();
        var jobId = store.Enqueue(CreateJob(Guid.NewGuid().ToString()));
        store.SetState(jobId, "Leased");

        // Act
        store.SetState(jobId, "Running");

        // Assert
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Running", record.State);
    }

    [TestMethod]
    public void SetState_Completed_TransitionsFromRunning()
    {
        // Arrange
        var store = new JobStore();
        var jobId = store.Enqueue(CreateJob(Guid.NewGuid().ToString()));
        store.SetState(jobId, "Leased");
        store.SetState(jobId, "Running");

        // Act
        store.SetState(jobId, "Completed");

        // Assert
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Completed", record.State);
    }

    [TestMethod]
    public void SetState_Failed_TransitionsFromRunning()
    {
        // Arrange
        var store = new JobStore();
        var jobId = store.Enqueue(CreateJob(Guid.NewGuid().ToString()));
        store.SetState(jobId, "Leased");
        store.SetState(jobId, "Running");

        // Act
        store.SetState(jobId, "Failed");

        // Assert
        var record = store.GetAllRecords().Single(r => Guid.Parse(r.Job.JobId) == jobId);
        Assert.AreEqual("Failed", record.State);
    }

    [TestMethod]
    public void GetAllRecords_ReturnsAllJobsWithState()
    {
        // Arrange
        var store = new JobStore();
        var id1 = store.Enqueue(CreateJob(Guid.NewGuid().ToString()));
        var id2 = store.Enqueue(CreateJob(Guid.NewGuid().ToString()));

        store.SetState(id1, "Running");
        // id2 stays Queued

        // Act
        var records = store.GetAllRecords();

        // Assert
        Assert.AreEqual(2, records.Count);
        var r1 = records.Single(r => Guid.Parse(r.Job.JobId) == id1);
        var r2 = records.Single(r => Guid.Parse(r.Job.JobId) == id2);
        Assert.AreEqual("Running", r1.State);
        Assert.AreEqual("Queued", r2.State);
    }
}
