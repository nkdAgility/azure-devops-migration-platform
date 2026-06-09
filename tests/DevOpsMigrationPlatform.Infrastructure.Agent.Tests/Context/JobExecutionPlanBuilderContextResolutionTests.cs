// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobExecutionPlanBuilderContextResolutionTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveAnalyseContextForAnalyser_WhenEndpointPairAnalyser_ReturnsEndpointPairContext()
    {
        var builder = CreateBuilder();
        var context = builder.ResolveAnalyseContextForAnalyser(
            new EndpointPairAnalyserStub(),
            new Job(),
            BuildConfig(),
            PackageTestFactory.CreateLooseMock().Object,
            Mock.Of<IProgressSink>());

        Assert.IsInstanceOfType<EndpointPairAnalyseContext>(context);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveAnalyseContextForAnalyser_WhenOrganisationsAnalyser_ReturnsOrganisationsContext()
    {
        var builder = CreateBuilder();
        var context = builder.ResolveAnalyseContextForAnalyser(
            new OrganisationsAnalyserStub(),
            new Job(),
            BuildConfig(),
            PackageTestFactory.CreateLooseMock().Object,
            Mock.Of<IProgressSink>());

        Assert.IsInstanceOfType<OrganisationsAnalyseContext>(context);
        var orgContext = (OrganisationsAnalyseContext)context;
        Assert.AreEqual(1, orgContext.Organisations.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveAnalyseContextForAnalyser_WhenBothInterfacesImplemented_PrefersEndpointPair()
    {
        var builder = CreateBuilder();
        var context = builder.ResolveAnalyseContextForAnalyser(
            new DualAnalyserStub(),
            new Job(),
            BuildConfig(),
            PackageTestFactory.CreateLooseMock().Object,
            Mock.Of<IProgressSink>());

        Assert.IsInstanceOfType<EndpointPairAnalyseContext>(context);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveAnalyseContextForAnalyser_WhenBaseAnalyser_ReturnsBaseContext()
    {
        var builder = CreateBuilder();
        var context = builder.ResolveAnalyseContextForAnalyser(
            new BaseAnalyserStub(),
            new Job(),
            BuildConfig(),
            PackageTestFactory.CreateLooseMock().Object,
            Mock.Of<IProgressSink>());

        Assert.AreEqual(typeof(AnalyseContext), context.GetType());
    }

    private static JobExecutionPlanBuilder CreateBuilder()
    {
        var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
        var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
        phaseFactory.Setup(x => x.Create(It.IsAny<IPackageAccess>())).Returns(phaseService.Object);
        return new JobExecutionPlanBuilder(
            [],
            [],
            phaseFactory.Object,
            NullLogger<JobExecutionPlanBuilder>.Instance,
            package: PackageTestFactory.CreateLooseMock().Object);
    }

    private static IConfiguration BuildConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Url"] = "https://source.example",
                ["MigrationPlatform:Source:Project"] = "SourceProject",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Target:Url"] = "https://target.example",
                ["MigrationPlatform:Target:Project"] = "TargetProject",
                ["MigrationPlatform:Organisations:0:Type"] = "Simulated",
                ["MigrationPlatform:Organisations:0:Url"] = "https://org.example",
            })
            .Build();
    }

    private sealed class BaseAnalyserStub : IAnalyser
    {
        public string Name => "Base";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
    }

    private sealed class EndpointPairAnalyserStub : IEndpointPairAnalyser
    {
        public string Name => "EndpointPair";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public Task<TaskExecutionResult> AnalyseAsync(EndpointPairAnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
    }

    private sealed class OrganisationsAnalyserStub : IOrganisationsAnalyser
    {
        public string Name => "Organisations";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public Task<TaskExecutionResult> AnalyseAsync(OrganisationsAnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
    }

    private sealed class DualAnalyserStub : IEndpointPairAnalyser, IOrganisationsAnalyser
    {
        public string Name => "Dual";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public Task<TaskExecutionResult> AnalyseAsync(EndpointPairAnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> AnalyseAsync(OrganisationsAnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
    }
}
