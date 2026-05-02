// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Services;

[TestClass]
public sealed class InMemoryJobTaskStoreTests
{
    private static JobTaskList MakeList(params string[] ids)
    {
        var tasks = new List<JobTask>();
        for (int i = 0; i < ids.Length; i++)
            tasks.Add(new JobTask { Id = ids[i], Name = ids[i], Order = i, Status = JobTaskStatus.Pending });
        return new JobTaskList { Tasks = tasks.AsReadOnly() };
    }

    [TestMethod]
    public void Store_ThenGetLatest_ReturnsSameList()
    {
        var store = new InMemoryJobTaskStore();
        var jobId = Guid.NewGuid();
        var list = MakeList("export.identities", "export.workitems");

        store.Store(jobId, list);
        var result = store.GetLatest(jobId);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Tasks.Count);
    }

    [TestMethod]
    public void GetLatest_NoList_ReturnsNull()
    {
        var store = new InMemoryJobTaskStore();
        Assert.IsNull(store.GetLatest(Guid.NewGuid()));
    }

    [TestMethod]
    public void UpdateTask_ExistingTask_TransitionsStatus()
    {
        var store = new InMemoryJobTaskStore();
        var jobId = Guid.NewGuid();
        store.Store(jobId, MakeList("export.identities", "export.workitems"));
        var now = DateTimeOffset.UtcNow;

        store.UpdateTask(jobId, "export.identities", JobTaskStatus.Running, null, now);

        var result = store.GetLatest(jobId)!;
        var updated = result.Tasks[0];
        Assert.AreEqual(JobTaskStatus.Running, updated.Status);
        Assert.AreEqual(now, updated.StartedAt);
        Assert.IsNull(updated.CompletedAt);
    }

    [TestMethod]
    public void UpdateTask_CompletedStatus_SetsCompletedAt()
    {
        var store = new InMemoryJobTaskStore();
        var jobId = Guid.NewGuid();
        store.Store(jobId, MakeList("export.identities"));
        var now = DateTimeOffset.UtcNow;

        store.UpdateTask(jobId, "export.identities", JobTaskStatus.Completed, 42L, now);

        var task = store.GetLatest(jobId)!.Tasks[0];
        Assert.AreEqual(JobTaskStatus.Completed, task.Status);
        Assert.AreEqual(42L, task.CompletedCount);
        Assert.AreEqual(now, task.CompletedAt);
    }

    [TestMethod]
    public void UpdateTask_UnknownTaskId_OtherTasksUnchanged()
    {
        var store = new InMemoryJobTaskStore();
        var jobId = Guid.NewGuid();
        store.Store(jobId, MakeList("export.identities"));

        store.UpdateTask(jobId, "nonexistent.task", JobTaskStatus.Running, null, DateTimeOffset.UtcNow);

        var task = store.GetLatest(jobId)!.Tasks[0];
        Assert.AreEqual(JobTaskStatus.Pending, task.Status);
    }

    [TestMethod]
    public void Remove_RemovesListForJob()
    {
        var store = new InMemoryJobTaskStore();
        var jobId = Guid.NewGuid();
        store.Store(jobId, MakeList("export.identities"));
        store.Remove(jobId);

        Assert.IsNull(store.GetLatest(jobId));
    }
}
