// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests.ProjectLifecycle;

[TestClass]
public sealed class TfsProjectLifecycleServiceTests
{
    [TestMethod]
    public async Task CreateAndTeardown_ReturnSuccessfulOutcome()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new TfsProjectLifecycleProvider(
                createAction: (_, _) => Task.CompletedTask,
                teardownAction: (_, _) => Task.CompletedTask),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "tfs-run-1",
            ConnectorType = "TeamFoundationServer",
            NamePrefix = "tfs"
        });
        var tornDown = await sut.TeardownAsync(created);

        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, created.CreateResult);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, tornDown.TeardownResult);
    }
}
