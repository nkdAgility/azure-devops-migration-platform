// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.ControlPlane.Controllers;
using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Services;

/// <summary>
/// #169 / ADR-0035: the operator sees one parent job ID. <c>GET /jobs</c> lists the parent as a
/// Migrate job whose state is built from its children, so <c>manage status</c> and the TUI can
/// find it by the ID the CLI printed.
/// </summary>
[TestClass]
public class JobsControllerParentJobTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetAllJobs_ListsParentOnce_AsMigrate_WithStateFromChildren()
    {
        var store = new JobStore();
        var parentId = Guid.NewGuid();
        var export = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Export, ParentJobId = parentId.ToString() };
        var import = new Job { JobId = Guid.NewGuid().ToString(), Kind = JobKind.Import, ParentJobId = parentId.ToString(), DependsOnJobId = export.JobId };
        store.Enqueue(export);
        store.Enqueue(import);
        store.SetState(Guid.Parse(export.JobId), "Running");
        var controller = new JobsController(store, NullLogger<JobsController>.Instance);

        var summaries = (JobSummary[])((OkObjectResult)controller.GetAllJobs()).Value!;

        var parent = summaries.Single(s => s.JobId == parentId);
        Assert.AreEqual("Migrate", parent.Mode);
        Assert.AreEqual("Running", parent.State);
        Assert.AreEqual(3, summaries.Length, "Both children and the parent are listed.");
    }
}
