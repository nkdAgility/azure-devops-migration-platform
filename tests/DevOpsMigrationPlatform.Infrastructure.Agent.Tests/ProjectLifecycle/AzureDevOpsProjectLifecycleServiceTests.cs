// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[TestClass]
public sealed class AzureDevOpsProjectLifecycleServiceTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CreateAndTeardown_ReturnSuccessfulLifecycleOutcomes()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new AzureDevOpsProjectLifecycleProvider(
                createAction: (_, _) => Task.CompletedTask,
                teardownAction: (_, _) => Task.CompletedTask),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "run-ado",
            ConnectorType = "AzureDevOpsServices",
            NamePrefix = "ado",
            Endpoint = new OrganisationEndpoint { Type = "AzureDevOpsServices", ResolvedUrl = "https://dev.azure.com/example" }
        });

        var tornDown = await sut.TeardownAsync(created);

        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, created.CreateResult);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, tornDown.TeardownResult);
        StringAssert.StartsWith(created.ProjectName, "ado-azuredevopsservices-run-ado-");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task Create_UsesExplicitProjectNameWhenProvided()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new AzureDevOpsProjectLifecycleProvider(createAction: (_, _) => Task.CompletedTask),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = Guid.NewGuid().ToString("N"),
            ConnectorType = "AzureDevOpsServices",
            ProjectName = "explicit-project-name"
        });

        Assert.AreEqual("explicit-project-name", created.ProjectName);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task Create_WhenProvisioningFails_ReturnsFailedRecordImmediately()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new AzureDevOpsProjectLifecycleProvider(
                createAction: (_, _) => throw new InvalidOperationException("provisioning failed")),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "run-fail",
            ConnectorType = "AzureDevOpsServices",
            ProjectName = "fail-project"
        });

        Assert.AreEqual(ProjectLifecycleCreateResult.Failed, created.CreateResult);
        StringAssert.Contains(created.CreateFailureReason!, "provisioning failed");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task Create_WithExplicitProcessName_AndTeardownCapturesLatency()
    {
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new AzureDevOpsProjectLifecycleProvider(
                createAction: (_, _) => Task.CompletedTask,
                teardownAction: (_, _) => Task.CompletedTask),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "run-process",
            ConnectorType = "AzureDevOpsServices",
            ProcessName = "Agile",
            Endpoint = new OrganisationEndpoint
            {
                Type = "AzureDevOpsServices",
                ResolvedUrl = "https://dev.azure.com/example"
            }
        });
        var tornDown = await sut.TeardownAsync(created);

        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, created.CreateResult);
        Assert.IsTrue(tornDown.TeardownLatency.HasValue);
    }
}
