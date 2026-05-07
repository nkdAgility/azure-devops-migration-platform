// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobExecutionPlanBuilderTests
{
    // The four standard export/import modules in execution order.
    private static readonly string[] StandardModuleNames = ["Identities", "Nodes", "Teams", "WorkItems"];

    private static IModule MockModule(string name, params ModuleDependency[] dependsOn)
    {
        var m = new Mock<IModule>(MockBehavior.Loose);
        m.SetupGet(x => x.Name).Returns(name);
        m.SetupGet(x => x.DependsOn).Returns((IReadOnlyList<ModuleDependency>)dependsOn);
        m.SetupGet(x => x.SupportsExport).Returns(true);
        m.SetupGet(x => x.SupportsImport).Returns(true);
        return m.Object;
    }

    private static JobExecutionPlanBuilder CreateBuilder(IEnumerable<IModule>? modules = null)
    {
        var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
        var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
        phaseService
            .Setup(s => s.ReadPhaseRecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPhaseRecord());
        phaseFactory
            .Setup(f => f.Create(It.IsAny<IStateStore>()))
            .Returns(phaseService.Object);
        var moduleList = modules?.ToList()
            ?? StandardModuleNames.Select(n => MockModule(n)).ToList();
        return new JobExecutionPlanBuilder(moduleList, [], phaseFactory.Object, NullLogger<JobExecutionPlanBuilder>.Instance);
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
        Assert.IsTrue(plan.Tasks[0].Id.StartsWith("export.identities"), $"Expected identities first but got: {plan.Tasks[0].Id}");
        Assert.IsTrue(plan.Tasks[3].Id.StartsWith("export.workitems"), $"Expected workitems last but got: {plan.Tasks[3].Id}");
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
        Assert.IsTrue(plan.Tasks[0].Id.StartsWith("import.identities"), $"Expected identities first but got: {plan.Tasks[0].Id}");
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
    public async Task BuildPlanAsync_DisabledModule_DoesNotCreateTask()
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

        // Identities is disabled, so it should not have a task
        Assert.IsFalse(plan.Tasks.Any(t => t.Id.Contains("identities")),
            "Disabled Identities module should not have a task");

        // Only 3 enabled modules should have export tasks
        Assert.AreEqual(3, plan.Tasks.Count(t => t.Phase == "Export"),
            "Should have 3 export tasks (Nodes, Teams, WorkItems)");
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
        stateStore
            .Setup(s => s.ExistsAsync(PackagePaths.InventoryCompleteFile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore.Object, CancellationToken.None);

        var workItemsTask = plan.Tasks[3];
        Assert.IsTrue(workItemsTask.Id.StartsWith("export.workitems"), $"Expected workitems task but got: {workItemsTask.Id}");
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

    // ── BuildAndSaveAsync resume / completed-plan regression tests ───────────

    /// <summary>
    /// Regression guard: when all persisted tasks are Completed/Skipped/Failed,
    /// BuildAndSaveAsync must return the existing plan unchanged — it must NOT archive
    /// it or build a fresh plan. A fresh plan would reset task statuses to Pending,
    /// causing every module to re-execute.
    /// </summary>
    [TestMethod]
    public async Task BuildAndSaveAsync_AllTasksCompleted_ReturnsSamePlanWithoutRebuild()
    {
        // Arrange: a completed plan already in the state store
        var completedPlan = new JobTaskList
        {
            ForKind = JobKind.Export,
            Tasks = new[]
            {
                MakeTask("export.identities", JobTaskStatus.Completed),
                MakeTask("export.nodes",      JobTaskStatus.Completed),
                MakeTask("export.teams",      JobTaskStatus.Completed),
                MakeTask("export.workitems",  JobTaskStatus.Completed)
            }.ToList().AsReadOnly(),
            PushedAt = DateTimeOffset.UtcNow
        };

        var stateStore = new InMemoryStateStore();
        await stateStore.WriteAsync(
            ".migration/plan.json",
            JsonSerializer.Serialize(completedPlan),
            CancellationToken.None);

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAndSaveAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore, CancellationToken.None);

        // Assert: task IDs and statuses must be exactly the completed plan's
        Assert.AreEqual(4, result.Tasks.Count, "Task count must not change on resume of a completed plan");
        Assert.IsTrue(result.Tasks.All(t => t.Status == JobTaskStatus.Completed),
            "All tasks must remain Completed — the plan must not be rebuilt from scratch");
    }

    /// <summary>
    /// Regression guard: if every task is Skipped, the plan is still terminal.
    /// BuildAndSaveAsync must return it as-is, not rebuild.
    /// </summary>
    [TestMethod]
    public async Task BuildAndSaveAsync_AllTasksSkipped_ReturnsSamePlanWithoutRebuild()
    {
        var skippedPlan = new JobTaskList
        {
            ForKind = JobKind.Export,
            Tasks = new[]
            {
                MakeTask("export.identities", JobTaskStatus.Skipped),
                MakeTask("export.nodes",      JobTaskStatus.Skipped),
                MakeTask("export.teams",      JobTaskStatus.Skipped),
                MakeTask("export.workitems",  JobTaskStatus.Skipped)
            }.ToList().AsReadOnly(),
            PushedAt = DateTimeOffset.UtcNow
        };

        var stateStore = new InMemoryStateStore();
        await stateStore.WriteAsync(
            ".migration/plan.json",
            JsonSerializer.Serialize(skippedPlan),
            CancellationToken.None);

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var builder = CreateBuilder();

        var result = await builder.BuildAndSaveAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore, CancellationToken.None);

        Assert.AreEqual(4, result.Tasks.Count, "Task count must not change");
        Assert.IsTrue(result.Tasks.All(t => t.Status == JobTaskStatus.Skipped),
            "All tasks must remain Skipped — plan must not be rebuilt");
    }

    /// <summary>
    /// Regression guard: a partially-completed plan (some Pending tasks remain)
    /// must be resumed, not rebuilt. The Pending tasks must keep their Pending status
    /// and be re-executed; the Completed tasks must not be reset.
    /// </summary>
    [TestMethod]
    public async Task BuildAndSaveAsync_PartiallyCompletedPlan_ReturnsExistingPlanWithMixedStatuses()
    {
        var partialPlan = new JobTaskList
        {
            ForKind = JobKind.Export,
            Tasks = new[]
            {
                MakeTask("export.identities", JobTaskStatus.Completed),
                MakeTask("export.nodes",      JobTaskStatus.Completed),
                MakeTask("export.teams",      JobTaskStatus.Pending),   // not yet done
                MakeTask("export.workitems",  JobTaskStatus.Pending)
            }.ToList().AsReadOnly(),
            PushedAt = DateTimeOffset.UtcNow
        };

        var stateStore = new InMemoryStateStore();
        await stateStore.WriteAsync(
            ".migration/plan.json",
            JsonSerializer.Serialize(partialPlan),
            CancellationToken.None);

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var builder = CreateBuilder();

        var result = await builder.BuildAndSaveAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore, CancellationToken.None);

        Assert.AreEqual(4, result.Tasks.Count);
        Assert.AreEqual(JobTaskStatus.Completed, result.Tasks.First(t => t.Id == "export.identities").Status,
            "Already-Completed task must stay Completed");
        Assert.AreEqual(JobTaskStatus.Pending, result.Tasks.First(t => t.Id == "export.teams").Status,
            "Pending task must remain Pending on resume");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JobTask MakeTask(string id, JobTaskStatus status) => new()
    {
        Id = id,
        Name = id,
        Phase = "Export",
        TaskKind = TaskKind.Export,
        Status = status
    };

    private sealed class InMemoryStateStore : IStateStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> ReadAsync(string key, CancellationToken ct)
        {
            _data.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task WriteAsync(string key, string content, CancellationToken ct)
        {
            _data[key] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct)
            => Task.FromResult(_data.ContainsKey(key));

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            _data.Remove(key);
            return Task.CompletedTask;
        }
    }
}
