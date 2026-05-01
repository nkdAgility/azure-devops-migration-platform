using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobExecutionPlanBuilderTests
{
    private static JobExecutionPlanBuilder CreateBuilder()
    {
        var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
        var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
        phaseService
            .Setup(s => s.ReadPhaseRecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPhaseRecord());
        phaseFactory
            .Setup(f => f.Create(It.IsAny<IStateStore>()))
            .Returns(phaseService.Object);
        var modules = new List<IModule>(); // Empty for basic tests
        return new JobExecutionPlanBuilder(modules, phaseFactory.Object, NullLogger<JobExecutionPlanBuilder>.Instance);
    }

    private static IConfiguration AllEnabledConfig()
    {
        var dict = new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
            ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
            ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
            ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [TestMethod]
    public async Task BuildPlanAsync_ExportKind_Returns4ExportTasks()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore.Object, CancellationToken.None);

        Assert.AreEqual(4, plan.Tasks.Count);
        Assert.IsTrue(plan.Tasks.Count > 0);
        Assert.AreEqual("export.identities", plan.Tasks[0].Id);
        Assert.AreEqual("export.workitems", plan.Tasks[3].Id);
        Assert.AreEqual("Export", plan.Tasks[0].Phase);
    }

    [TestMethod]
    public async Task BuildPlanAsync_ImportKind_Returns4ImportTasks()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Import, store.Object, stateStore.Object, CancellationToken.None);

        Assert.AreEqual(4, plan.Tasks.Count);
        Assert.AreEqual("import.identities", plan.Tasks[0].Id);
        Assert.AreEqual("Import", plan.Tasks[0].Phase);
    }

    [TestMethod]
    public async Task BuildPlanAsync_MigrateKind_Returns8Tasks()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Migrate, store.Object, stateStore.Object, CancellationToken.None);

        Assert.AreEqual(8, plan.Tasks.Count);
    }

    [TestMethod]
    public async Task BuildPlanAsync_DisabledModule_MarksTaskAsSkipped()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Modules:Identities:Enabled"] = "false",
                ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
                ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
                ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
            })
            .Build();

        var plan = await builder.BuildPlanAsync(
            config, JobKind.Export, store.Object, stateStore.Object, CancellationToken.None);

        var identitiesTask = plan.Tasks[0];
        Assert.AreEqual(JobTaskStatus.Skipped, identitiesTask.Status);
        Assert.IsNotNull(identitiesTask.SkipReason);
    }

    [TestMethod]
    public async Task BuildPlanAsync_InventoryJsonPresent_SetsKnownTotalOnWorkItemsExport()
    {
        var builder = CreateBuilder();
        var inventoryJson = JsonSerializer.Serialize(new
        {
            Totals = new { WorkItems = 500L, Revisions = 1500L, Repos = 2, Projects = 3 },
            Organisations = Array.Empty<object>()
        });
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store
            .Setup(s => s.ReadAsync("inventory.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventoryJson);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore.Object, CancellationToken.None);

        var workItemsTask = plan.Tasks[3];
        Assert.AreEqual("export.workitems", workItemsTask.Id);
        Assert.AreEqual(500L, workItemsTask.KnownTotal);
    }

    [TestMethod]
    public async Task BuildPlanAsync_AllTasksHaveAscendingOrder()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Migrate, store.Object, stateStore.Object, CancellationToken.None);

        for (int i = 0; i < plan.Tasks.Count; i++)
            Assert.AreEqual(i, plan.Tasks[i].Order);
    }
}
