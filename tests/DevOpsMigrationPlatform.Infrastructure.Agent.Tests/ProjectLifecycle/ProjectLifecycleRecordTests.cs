// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[TestClass]
public sealed class ProjectLifecycleRecordTests
{
    [TestMethod]
    public void CreateFailed_CapturesFailureReasonAndSkippedTeardown()
    {
        var context = new ProjectLifecycleContext
        {
            RunId = "run-1",
            ConnectorType = "AzureDevOpsServices",
            ProjectName = "run-1-project"
        };

        var record = ProjectLifecycleRecord.CreateFailed(context, "permission denied");

        Assert.AreEqual(ProjectLifecycleCreateResult.Failed, record.CreateResult);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Skipped, record.TeardownResult);
        Assert.AreEqual("permission denied", record.CreateFailureReason);
    }

    [TestMethod]
    public void Record_AllowsBlockingReasonAndPartialCleanupDetails()
    {
        var record = new ProjectLifecycleRecord
        {
            RunId = "run-2",
            ConnectorType = "TeamFoundationServer",
            ProjectName = "run-2-project",
            CreateResult = ProjectLifecycleCreateResult.Succeeded,
            TeardownResult = ProjectLifecycleTeardownResult.Failed,
            TeardownBlockingReason = "permission denied",
            PartialCleanupDetail = "contributors removed; project delete denied"
        };

        Assert.AreEqual("permission denied", record.TeardownBlockingReason);
        Assert.AreEqual("contributors removed; project delete denied", record.PartialCleanupDetail);
    }
}
