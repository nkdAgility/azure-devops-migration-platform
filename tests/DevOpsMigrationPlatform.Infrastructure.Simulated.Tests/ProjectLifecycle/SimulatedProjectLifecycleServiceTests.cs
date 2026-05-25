// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.ProjectLifecycle;

[TestClass]
public sealed class SimulatedProjectLifecycleServiceTests
{
    [TestMethod]
    public async Task CreateThenTeardown_Succeeds()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new SimulatedProjectLifecycleProvider(),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "sim-run-1",
            ConnectorType = "Simulated",
            NamePrefix = "sim"
        });
        var tornDown = await sut.TeardownAsync(created);

        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, created.CreateResult);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, tornDown.TeardownResult);
    }

    [TestMethod]
    public async Task TeardownUnknownProject_ReturnsFailedOutcome()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new SimulatedProjectLifecycleProvider(),
            NullLogger<ProjectLifecycleService>.Instance);
        var tornDown = await sut.TeardownAsync(new ProjectLifecycleRecord
        {
            RunId = "sim-run-2",
            ConnectorType = "Simulated",
            ProjectName = "missing",
            ProjectOwnedByRun = true,
            CreateResult = ProjectLifecycleCreateResult.Succeeded
        });

        Assert.AreEqual(ProjectLifecycleTeardownResult.Failed, tornDown.TeardownResult);
        StringAssert.Contains(tornDown.TeardownBlockingReason!, "does not exist");
    }
}
