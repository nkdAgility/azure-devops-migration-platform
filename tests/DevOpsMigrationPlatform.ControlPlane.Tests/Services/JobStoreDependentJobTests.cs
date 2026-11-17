// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Services;

/// <summary>
/// #169 / ADR-0035: a cross-agent migration is a parent job with an Export child and an
/// Import child that depends on it. The Control Plane routes each child by its own
/// connectors and only holds a dependent child until the job it depends on completes.
/// </summary>
[TestClass]
public class JobStoreDependentJobTests
{
    private static readonly ConnectorType[] TfsAgent = [ConnectorType.TeamFoundationServer];
    private static readonly ConnectorType[] AdoAgent = [ConnectorType.AzureDevOps, ConnectorType.Simulated];
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static (Job Export, Job Import, string ParentId) TfsToAdoChildren()
    {
        var parentId = Guid.NewGuid().ToString();
        var export = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Export,
            Connectors = [ConnectorType.TeamFoundationServer],
            ParentJobId = parentId
        };
        var import = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Import,
            Connectors = [ConnectorType.AzureDevOps],
            ParentJobId = parentId,
            DependsOnJobId = export.JobId
        };
        return (export, import, parentId);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task DequeueAsync_ExportChild_GoesToTfsAgent()
    {
        var store = new JobStore();
        var (export, import, _) = TfsToAdoChildren();
        store.Enqueue(export);
        store.Enqueue(import);

        var leased = await store.DequeueAsync(Short, TfsAgent, CancellationToken.None);

        Assert.IsNotNull(leased);
        Assert.AreEqual(export.JobId, leased.JobId);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task DequeueAsync_ImportChild_IsHeldWhileExportChildIsNotCompleted()
    {
        var store = new JobStore();
        var (export, import, _) = TfsToAdoChildren();
        store.Enqueue(export);
        store.Enqueue(import);
        store.SetState(Guid.Parse(export.JobId), "Running");

        var leased = await store.DequeueAsync(Short, AdoAgent, CancellationToken.None);

        Assert.IsNull(leased, "The Import child must wait for the Export child to complete.");
        Assert.AreEqual("Queued", store.GetAllRecords().Single(r => r.Job.JobId == import.JobId).State);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task DequeueAsync_ImportChild_GoesToAdoAgentOnceExportChildCompletes()
    {
        var store = new JobStore();
        var (export, import, _) = TfsToAdoChildren();
        store.Enqueue(export);
        store.Enqueue(import);
        var exportLease = await store.DequeueAsync(Short, TfsAgent, CancellationToken.None);
        Assert.IsNotNull(exportLease);

        store.SetState(Guid.Parse(export.JobId), "Completed");
        var leased = await store.DequeueAsync(Short, AdoAgent, CancellationToken.None);

        Assert.IsNotNull(leased);
        Assert.AreEqual(import.JobId, leased.JobId);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task SetState_ExportChildFailed_FailsImportChildWithoutRunningIt()
    {
        var store = new JobStore();
        var (export, import, _) = TfsToAdoChildren();
        store.Enqueue(export);
        store.Enqueue(import);

        store.SetState(Guid.Parse(export.JobId), "Failed");
        var leased = await store.DequeueAsync(Short, AdoAgent, CancellationToken.None);

        Assert.IsNull(leased, "A child whose dependency failed must never be leased.");
        Assert.AreEqual("Failed", store.GetAllRecords().Single(r => r.Job.JobId == import.JobId).State);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetParentState_IsBuiltFromChildren()
    {
        var store = new JobStore();
        var (export, import, parentId) = TfsToAdoChildren();
        store.Enqueue(export);
        store.Enqueue(import);
        var parent = Guid.Parse(parentId);

        Assert.AreEqual("Queued", store.GetParentState(parent));

        store.SetState(Guid.Parse(export.JobId), "Running");
        Assert.AreEqual("Running", store.GetParentState(parent));

        store.SetState(Guid.Parse(export.JobId), "Completed");
        Assert.AreEqual("Running", store.GetParentState(parent), "Parent is not done until every child is.");

        store.SetState(Guid.Parse(import.JobId), "Completed");
        Assert.AreEqual("Completed", store.GetParentState(parent));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetParentState_IsFailedWhenAnyChildFails()
    {
        var store = new JobStore();
        var (export, import, parentId) = TfsToAdoChildren();
        store.Enqueue(export);
        store.Enqueue(import);

        store.SetState(Guid.Parse(export.JobId), "Failed");

        Assert.AreEqual("Failed", store.GetParentState(Guid.Parse(parentId)));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetParentState_UnknownParent_IsNull()
    {
        var store = new JobStore();
        Assert.IsNull(store.GetParentState(Guid.NewGuid()));
    }
}
