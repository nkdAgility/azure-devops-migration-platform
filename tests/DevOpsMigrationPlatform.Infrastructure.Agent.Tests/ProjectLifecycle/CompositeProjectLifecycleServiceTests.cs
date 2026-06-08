// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[TestClass]
public sealed class ProjectLifecycleServiceTests
{
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task CreateAsync_DispatchesToConnectorRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FakeLifecycleProvider>();
        services.AddSingleton(new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)));
        var provider = services.BuildServiceProvider();
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new[] { new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)) },
            provider,
            NullLogger<ProjectLifecycleService>.Instance);

        var record = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "run-1",
            ConnectorType = "Simulated",
            ProjectName = "proj-a"
        });

        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, record.CreateResult);
        Assert.AreEqual("proj-a", record.ProjectName);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task TeardownAsync_BlocksForeignProjectDeletion()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FakeLifecycleProvider>();
        services.AddSingleton(new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)));
        var provider = services.BuildServiceProvider();
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new[] { new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)) },
            provider,
            NullLogger<ProjectLifecycleService>.Instance);

        var result = await sut.TeardownAsync(new ProjectLifecycleRecord
        {
            RunId = "run-2",
            ConnectorType = "Simulated",
            ProjectName = "foreign-project",
            ProjectOwnedByRun = false,
            CreateResult = ProjectLifecycleCreateResult.Succeeded
        });

        Assert.AreEqual(ProjectLifecycleTeardownResult.Failed, result.TeardownResult);
        StringAssert.Contains(result.TeardownBlockingReason!, "not owned");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task ExecuteWithGuaranteedTeardownAsync_AttemptsTeardownWhenExecutionFails()
    {
        FakeLifecycleProvider.Reset();
        var services = new ServiceCollection();
        services.AddSingleton<FakeLifecycleProvider>();
        services.AddSingleton(new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)));
        var provider = services.BuildServiceProvider();
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new[] { new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)) },
            provider,
            NullLogger<ProjectLifecycleService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.ExecuteWithGuaranteedTeardownAsync(
                new ProjectLifecycleContext
                {
                    RunId = "run-3",
                    ConnectorType = "Simulated",
                    ProjectName = "proj-b"
                },
                (_, _) => throw new InvalidOperationException("boom")));

        Assert.IsTrue(FakeLifecycleProvider.TeardownCount > 0);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task CreateAsync_PreservesConnectorParityInLifecycleRecord()
    {
        foreach (var connector in new[] { "Simulated", "AzureDevOpsServices", "TeamFoundationServer" })
        {
            var services = new ServiceCollection();
            services.AddSingleton<FakeLifecycleProvider>();
            services.AddSingleton(new KeyedProjectLifecycleProvider(connector, typeof(FakeLifecycleProvider)));
            var provider = services.BuildServiceProvider();
            var sut = new ProjectLifecycleService(
                new ProjectLifecycleNameGenerator(),
                new[] { new KeyedProjectLifecycleProvider(connector, typeof(FakeLifecycleProvider)) },
                provider,
                NullLogger<ProjectLifecycleService>.Instance);

            var record = await sut.CreateAsync(new ProjectLifecycleContext
            {
                RunId = "run-parity",
                ConnectorType = connector,
                ProjectName = "proj-parity"
            });

            Assert.AreEqual(connector, record.ConnectorType);
        }
    }

    // --- Scenarios from ephemeral-project-lifecycle.feature ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task EphemeralLifecycle_SimulatedConnector_CreateAndTeardownBothSucceed()
    {
        // Scenario US1: Eligible run creates and tears down project successfully
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new SimulatedProjectLifecycleProvider(),
            NullLogger<ProjectLifecycleService>.Instance);

        var created = await sut.CreateAsync(new ProjectLifecycleContext
        {
            RunId = Guid.NewGuid().ToString("N"),
            ConnectorType = "Simulated",
            NamePrefix = "bdd",
            Endpoint = new Abstractions.Organisations.OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://example.test" }
        });

        var tornDown = await sut.TeardownAsync(created);

        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, created.CreateResult, "Setup should succeed");
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, tornDown.TeardownResult, "Teardown should succeed");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task EphemeralLifecycle_TeardownIsAttemptedWhenTestExecutionFails()
    {
        // Scenario US2: Teardown is attempted when test execution fails
        FakeLifecycleProvider.Reset();
        var services = new ServiceCollection();
        services.AddSingleton<FakeLifecycleProvider>();
        services.AddSingleton(new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)));
        var provider = services.BuildServiceProvider();
        var sut = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new[] { new KeyedProjectLifecycleProvider("Simulated", typeof(FakeLifecycleProvider)) },
            provider,
            NullLogger<ProjectLifecycleService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.ExecuteWithGuaranteedTeardownAsync(
                new ProjectLifecycleContext
                {
                    RunId = "run-fail-exec",
                    ConnectorType = "Simulated",
                    ProjectName = "proj-fail"
                },
                (_, _) => throw new InvalidOperationException("execution failed")));

        Assert.IsTrue(FakeLifecycleProvider.TeardownCount > 0, "Teardown should be attempted");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void EphemeralLifecycle_EligibilityRespects_AzureDevOpsServicesConnector()
    {
        // Scenario US3 row 1: AzureDevOpsServices connector is eligible
        var eligibility = new LifecycleEligibilityFlag
        {
            IsEnabled = true,
            Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AzureDevOpsServices" }
        };

        Assert.IsTrue(eligibility.IsEligibleForConnector("AzureDevOpsServices"));
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void EphemeralLifecycle_EligibilityRespects_TeamFoundationServerConnector()
    {
        // Scenario US3 row 2: TeamFoundationServer connector is eligible
        var eligibility = new LifecycleEligibilityFlag
        {
            IsEnabled = true,
            Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TeamFoundationServer" }
        };

        Assert.IsTrue(eligibility.IsEligibleForConnector("TeamFoundationServer"));
    }

    private sealed class FakeLifecycleProvider : IProjectLifecycleProvider
    {
        public static int TeardownCount { get; private set; }
        public static void Reset() => TeardownCount = 0;

        public Task CreateActionAsync(ProjectLifecycleContext context, string projectName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken = default)
        {
            TeardownCount++;
            return Task.CompletedTask;
        }
    }
}
