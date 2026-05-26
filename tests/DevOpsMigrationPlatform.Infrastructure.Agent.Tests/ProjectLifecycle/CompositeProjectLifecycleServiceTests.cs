// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[TestClass]
public sealed class ProjectLifecycleServiceTests
{
    [TestMethod]
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
