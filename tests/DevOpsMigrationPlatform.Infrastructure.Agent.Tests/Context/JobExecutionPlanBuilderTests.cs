// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
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
    public async Task BuildPlanAsync_ExportKind_PopulatesOrderedPhaseSummaries()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Export, store.Object, stateStore.Object, CancellationToken.None);

        Assert.AreEqual(1, plan.Phases.Count, "Export plans should expose a single Export phase summary");
        Assert.AreEqual("Export", plan.Phases[0].Name);
        Assert.AreEqual(0, plan.Phases[0].Order);
        CollectionAssert.AreEqual(
            plan.Tasks.OrderBy(t => t.Order).Select(t => t.Id).ToArray(),
            plan.Phases[0].TaskIds.ToArray(),
            "Phase summary should reference export tasks in plan order");
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
    public async Task BuildPlanAsync_MigrateKind_ContainsOrderedExportThenImportTaskSets()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Migrate, store.Object, stateStore.Object, CancellationToken.None);

        var exportTasks = plan.Tasks.Where(t => string.Equals(t.Phase, "Export", StringComparison.Ordinal)).ToList();
        var importTasks = plan.Tasks.Where(t => string.Equals(t.Phase, "Import", StringComparison.Ordinal)).ToList();

        Assert.AreEqual(4, exportTasks.Count, "Expected four export tasks for the standard module set");
        Assert.AreEqual(4, importTasks.Count, "Expected four import tasks for the standard module set");

        var expectedExportPrefixes = new[] { "export.identities", "export.nodes", "export.teams", "export.workitems" };
        foreach (var prefix in expectedExportPrefixes)
        {
            Assert.IsTrue(exportTasks.Any(t => t.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                $"Missing expected export task with prefix {prefix}");
        }

        var expectedImportPrefixes = new[] { "import.identities", "import.nodes", "import.teams", "import.workitems" };
        foreach (var prefix in expectedImportPrefixes)
        {
            Assert.IsTrue(importTasks.Any(t => t.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                $"Missing expected import task with prefix {prefix}");
        }

        var maxExportOrder = exportTasks.Max(t => t.Order);
        var minImportOrder = importTasks.Min(t => t.Order);
        Assert.IsTrue(maxExportOrder < minImportOrder,
            "Export tasks must be ordered before import tasks in migrate plans");

        Assert.AreEqual(plan.Tasks.Count, plan.Tasks.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Task IDs must be unique across phases");

        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            Assert.AreEqual(i, plan.Tasks[i].Order,
                "Task order must remain contiguous and deterministic");
        }
    }

    [TestMethod]
    public async Task BuildPlanAsync_MigrateKind_PopulatesOrderedPhaseSummaries()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);

        var plan = await builder.BuildPlanAsync(
            AllEnabledConfig(), JobKind.Migrate, store.Object, stateStore.Object, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "Export", "Import" },
            plan.Phases.OrderBy(p => p.Order).Select(p => p.Name).ToArray(),
            "Migrate plans should expose phases in canonical order");

        foreach (var phase in plan.Phases)
        {
            var expectedTaskIds = plan.Tasks
                .Where(t => string.Equals(t.Phase, phase.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Order)
                .Select(t => t.Id)
                .ToArray();

            CollectionAssert.AreEqual(
                expectedTaskIds,
                phase.TaskIds.ToArray(),
                $"Phase summary '{phase.Name}' should reference only its own tasks in plan order");
        }
    }

    [TestMethod]
    public async Task BuildPlanAsync_MigrateKind_UsesGeneratorProjectNamesWhenSourceProjectIsNotExplicitlyConfigured()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Generator:Projects:0:Name"] = "RoundtripProject",
                ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
                ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
                ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
                ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
            })
            .Build();

        var plan = await builder.BuildPlanAsync(
            config, JobKind.Migrate, store.Object, stateStore.Object, CancellationToken.None);

        var migrateTasks = plan.Tasks
            .Where(t => string.Equals(t.Phase, "Export", StringComparison.Ordinal) || string.Equals(t.Phase, "Import", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(migrateTasks.Count > 0, "Migrate plan should contain export/import tasks.");
        Assert.IsTrue(migrateTasks.All(t => string.Equals(t.ProjectName, "RoundtripProject", StringComparison.Ordinal)),
            "Migrate export/import tasks should inherit generator project names when explicit source/target projects are absent.");
    }

    [TestMethod]
    public async Task BuildPlanAsync_ImportKind_UsesPackagedProjectNamesWhenTargetProjectIsNotExplicitlyConfigured()
    {
        var builder = CreateBuilder();
        var store = new Mock<IArtefactStore>(MockBehavior.Strict);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "Import",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
                ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
                ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
                ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
            })
            .Build();

        store
            .Setup(s => s.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
            .Returns(EnumeratePathsAsync([
                "migration-config.json",
                ".migration/plan.json",
                "simulated/PackagedProject/Nodes/source-tree.json",
                "simulated/PackagedProject/WorkItems/00000000000001-1-0/revision.json"
            ]));

        var plan = await builder.BuildPlanAsync(
            config, JobKind.Import, store.Object, stateStore.Object, CancellationToken.None);

        var importTasks = plan.Tasks
            .Where(t => string.Equals(t.Phase, "Import", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(importTasks.Count > 0, "Import plan should contain import tasks.");
        Assert.IsTrue(importTasks.All(t => string.Equals(t.ProjectName, "PackagedProject", StringComparison.Ordinal)),
            "Import tasks should inherit packaged project names when target/source projects are absent from config.");
    }

    [TestMethod]
    public async Task BuildPlanAsync_ImportKind_UsesPackageRootNameWhenFixtureIsAlreadyProjectScoped()
    {
        var builder = CreateBuilder();
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "Import",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
                ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
                ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
                ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
            })
            .Build();

        var packageRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SimulatedProject");
        Directory.CreateDirectory(Path.Combine(packageRoot, "Nodes"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "WorkItems", "2024-01-15", "00000000000001-1-0"));
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "Nodes", "source-tree.json"), "{}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "WorkItems", "2024-01-15", "00000000000001-1-0", "revision.json"), "{}", CancellationToken.None);

        try
        {
            var artefactStore = new FileSystemArtefactStore(packageRoot);

            var plan = await builder.BuildPlanAsync(
                config, JobKind.Import, artefactStore, stateStore.Object, CancellationToken.None);

            var importTasks = plan.Tasks
                .Where(t => string.Equals(t.Phase, "Import", StringComparison.Ordinal))
                .ToList();

            Assert.IsTrue(importTasks.Count > 0, "Import plan should contain import tasks.");
            Assert.IsTrue(importTasks.All(t => string.Equals(t.ProjectName, "SimulatedProject", StringComparison.Ordinal)),
                "Import tasks should inherit the package root name when the fixture is already project-scoped.");
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(packageRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(packageRoot)!, recursive: true);
            }
        }
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

    private static async IAsyncEnumerable<string> EnumeratePathsAsync(
        IEnumerable<string> paths,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
            await Task.Yield();
        }
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
            Phases = new[]
            {
                new JobPhaseSummary
                {
                    Name = "Export",
                    Order = 0,
                    TaskIds = new[]
                    {
                        "export.identities",
                        "export.nodes",
                        "export.teams",
                        "export.workitems"
                    }
                }
            }.ToList().AsReadOnly(),
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
        Assert.AreEqual(1, result.Phases.Count, "Resume should preserve phase summaries from the persisted plan.");
        Assert.AreEqual("Export", result.Phases[0].Name);
        CollectionAssert.AreEqual(
            new[] { "export.identities", "export.nodes", "export.teams", "export.workitems" },
            result.Phases[0].TaskIds.ToArray(),
            "Resume should preserve phase membership when loading an existing plan.");
    }

    [TestMethod]
    public async Task BuildAndSaveAsync_ForKindMismatch_DeletesPlanAndBuildsFreshForRequestedKind()
    {
        // Arrange: persist an Export plan, then request Import.
        var stalePlan = new JobTaskList
        {
            ForKind = JobKind.Export,
            Tasks = new[]
            {
                MakeTask("export.identities", JobTaskStatus.Completed),
                MakeTask("export.nodes", JobTaskStatus.Completed),
                MakeTask("export.teams", JobTaskStatus.Completed),
                MakeTask("export.workitems", JobTaskStatus.Completed)
            }.ToList().AsReadOnly(),
            PushedAt = DateTimeOffset.UtcNow
        };

        var stateStore = new InMemoryStateStore();
        await stateStore.WriteAsync(
            ".migration/plan.json",
            JsonSerializer.Serialize(stalePlan),
            CancellationToken.None);

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAndSaveAsync(
            AllEnabledConfig(), JobKind.Import, store.Object, stateStore, CancellationToken.None);

        // Assert
        Assert.AreEqual(JobKind.Import, result.ForKind, "Returned plan must be stamped with the requested kind");
        Assert.IsTrue(result.Tasks.All(t => t.Phase == "Import"), "All tasks must be import-phase tasks");
        Assert.IsTrue(result.Tasks.All(t => t.Id.StartsWith("import.", StringComparison.OrdinalIgnoreCase)),
            "Fresh plan must not contain stale export task IDs");
        Assert.IsFalse(result.Tasks.Any(t => t.Id.StartsWith("export.", StringComparison.OrdinalIgnoreCase)),
            "Export task IDs from stale plan must be removed");
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
