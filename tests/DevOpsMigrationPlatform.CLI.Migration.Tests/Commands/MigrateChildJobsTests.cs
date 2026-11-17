// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// #169 / ADR-0035: Migrate mode submits one parent job as Export → Prepare → Import child
/// jobs. Each child demands only the connector of the side it touches, so a TFS source and
/// an Azure DevOps target can be served by two different agents.
/// </summary>
[TestClass]
public class MigrateChildJobsTests
{
    private static string Config(string sourceType, string targetType) => $$"""
        { "MigrationPlatform": {
            "Mode": "Migrate",
            "Source": { "Type": "{{sourceType}}" },
            "Target": { "Type": "{{targetType}}" } } }
        """;

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_TfsToAdo_ExportDemandsSourceOnly_PrepareAndImportDemandTargetOnly()
    {
        var jobs = MigrateChildJobs.Build(Config("TeamFoundationServer", "AzureDevOpsServices"));

        CollectionAssert.AreEqual(new[] { ConnectorType.TeamFoundationServer }, jobs.Export.Connectors);
        CollectionAssert.AreEqual(new[] { ConnectorType.AzureDevOps }, jobs.Prepare.Connectors);
        CollectionAssert.AreEqual(new[] { ConnectorType.AzureDevOps }, jobs.Import.Connectors);
    }

    private sealed class RecordingSubmitter : DevOpsMigrationPlatform.Abstractions.Jobs.IJobSubmissionClient
    {
        public System.Collections.Generic.List<string> Submitted { get; } = new();

        public System.Threading.Tasks.Task<Guid> SubmitAsync(DevOpsMigrationPlatform.Abstractions.Jobs.Job job, System.Threading.CancellationToken ct = default)
        {
            Submitted.Add(job.JobId);
            return System.Threading.Tasks.Task.FromResult(Guid.Parse(job.JobId));
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async System.Threading.Tasks.Task SubmitAllAsync_RegistersEveryChildBeforeAnyIsFollowed()
    {
        // PR #222 review: submitting Prepare and Import only after Export finished let the parent
        // read Completed after Export alone, and a Ctrl-C in between left them never submitted.
        var jobs = MigrateChildJobs.Build(Config("TeamFoundationServer", "AzureDevOpsServices"));
        var client = new RecordingSubmitter();

        await jobs.SubmitAllAsync(client, System.Threading.CancellationToken.None);

        // Dependents first, Export last: Export is what starts the migration, so it is only
        // submitted once the Control Plane already holds Prepare and Import behind it.
        CollectionAssert.AreEqual(new[] { jobs.Import.JobId, jobs.Prepare.JobId, jobs.Export.JobId }, client.Submitted);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_ChildrenShareOneParent_AndRunInOrder()
    {
        var jobs = MigrateChildJobs.Build(Config("TeamFoundationServer", "AzureDevOpsServices"));

        Assert.IsTrue(Guid.TryParse(jobs.ParentJobId, out _));
        Assert.AreEqual(jobs.ParentJobId, jobs.Export.ParentJobId);
        Assert.AreEqual(jobs.ParentJobId, jobs.Prepare.ParentJobId);
        Assert.AreEqual(jobs.ParentJobId, jobs.Import.ParentJobId);

        Assert.IsNull(jobs.Export.DependsOnJobId);
        Assert.AreEqual(jobs.Export.JobId, jobs.Prepare.DependsOnJobId);
        Assert.AreEqual(jobs.Prepare.JobId, jobs.Import.DependsOnJobId);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_SimulatedToSimulated_EachChildDemandsSimulated()
    {
        var jobs = MigrateChildJobs.Build(Config("Simulated", "Simulated"));

        CollectionAssert.AreEqual(new[] { ConnectorType.Simulated }, jobs.Export.Connectors);
        CollectionAssert.AreEqual(new[] { ConnectorType.Simulated }, jobs.Import.Connectors);
    }
}
