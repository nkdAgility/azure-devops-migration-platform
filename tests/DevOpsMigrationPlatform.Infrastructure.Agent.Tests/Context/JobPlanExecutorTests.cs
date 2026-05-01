using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobPlanExecutorTests
{
    [TestMethod]
    public async Task ExecuteExportPhaseAsync_AllModulesEnabled_RunsConcurrently()
    {
        // Arrange
        var startTimes = new List<DateTimeOffset>();
        var completeTimes = new List<DateTimeOffset>();
        var lockObj = new object();

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Identities", "Nodes", "Teams", "WorkItems" })
        {
            var module = new Mock<IModule>(MockBehavior.Loose);
            module.SetupGet(m => m.Name).Returns(name);
            module.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    lock (lockObj)
                    {
                        startTimes.Add(DateTimeOffset.UtcNow);
                    }
                    await Task.Delay(50); // Simulate some work
                    lock (lockObj)
                    {
                        completeTimes.Add(DateTimeOffset.UtcNow);
                    }
                });
            modules[name] = module.Object;
        }

        var plan = CreatePlan(new[]
        {
            CreateTask("export.identities", "Identities Export", "Export"),
            CreateTask("export.nodes", "Nodes Export", "Export"),
            CreateTask("export.teams", "Teams Export", "Export"),
            CreateTask("export.workitems", "WorkItems Export", "Export")
        });

        var executor = CreateExecutor();
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;
        var exportContext = new ExportContext();

        // Act
        var result = await executor.ExecuteExportPhaseAsync(plan, modules, exportContext, stateStore, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Export phase should succeed");
        Assert.AreEqual(4, startTimes.Count, "All 4 tasks should have started");

        // Check that at least 3 tasks started within 500 ms of each other (concurrent execution)
        var minStart = startTimes.Min();
        var concurrentStarts = startTimes.Count(t => (t - minStart).TotalMilliseconds < 500);
        Assert.IsTrue(concurrentStarts >= 3, $"At least 3 tasks should start within 500ms, but only {concurrentStarts} did");
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_WorkItemsDependsOnIdentities_WaitsForIdentities()
    {
        // Arrange
        var executionOrder = new List<string>();
        var lockObj = new object();

        var identitiesModule = new Mock<IModule>(MockBehavior.Loose);
        identitiesModule.SetupGet(m => m.Name).Returns("Identities");
        identitiesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(100); // Simulate work
                lock (lockObj)
                {
                    executionOrder.Add("Identities");
                }
            });

        var workItemsModule = new Mock<IModule>(MockBehavior.Loose);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("WorkItems");
                }
                return Task.CompletedTask;
            });

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identities"] = identitiesModule.Object,
            ["WorkItems"] = workItemsModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import"),
            CreateTask("import.workitems", "WorkItems Import", "Import", dependsOn: new[] { "import.identities" })
        });

        var executor = CreateExecutor();
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, stateStore, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Import phase should succeed");
        Assert.AreEqual(2, executionOrder.Count, "Both tasks should execute");
        Assert.AreEqual("Identities", executionOrder[0], "Identities should complete first");
        Assert.AreEqual("WorkItems", executionOrder[1], "WorkItems should complete after Identities");
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_IdentitiesFails_WorkItemsSkipped()
    {
        // Arrange
        var identitiesModule = new Mock<IModule>(MockBehavior.Loose);
        identitiesModule.SetupGet(m => m.Name).Returns("Identities");
        identitiesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated failure"));

        var workItemsModule = new Mock<IModule>(MockBehavior.Loose);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identities"] = identitiesModule.Object,
            ["WorkItems"] = workItemsModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import"),
            CreateTask("import.workitems", "WorkItems Import", "Import", dependsOn: new[] { "import.identities" })
        });

        var executor = CreateExecutor();
        var stateStore = new InMemoryStateStore();
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, stateStore, CancellationToken.None);

        // Assert
        Assert.IsFalse(result, "Import phase should fail");

        // Check that plan was persisted with correct statuses
        var persistedJson = await stateStore.ReadAsync(".migration/Checkpoints/plan.json", CancellationToken.None);
        Assert.IsNotNull(persistedJson, "Plan should be persisted");

        var persistedPlan = System.Text.Json.JsonSerializer.Deserialize<JobTaskList>(persistedJson);
        var identitiesTask = persistedPlan!.Tasks.First(t => t.Id == "import.identities");
        var workItemsTask = persistedPlan.Tasks.First(t => t.Id == "import.workitems");

        Assert.AreEqual(JobTaskStatus.Failed, identitiesTask.Status, "Identities task should be Failed");
        Assert.AreEqual(JobTaskStatus.Skipped, workItemsTask.Status, "WorkItems task should be Skipped");
        Assert.IsNotNull(workItemsTask.SkipReason, "WorkItems should have a skip reason");
        Assert.IsTrue(workItemsTask.SkipReason!.Contains("import.identities"), "Skip reason should mention the failed dependency");
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_DisabledDependency_DependentSkipped()
    {
        // Arrange
        var nodesModule = new Mock<IModule>(MockBehavior.Loose);
        nodesModule.SetupGet(m => m.Name).Returns("Nodes");
        nodesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nodes"] = nodesModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import", status: JobTaskStatus.Skipped, skipReason: "Disabled"),
            CreateTask("import.nodes", "Nodes Import", "Import", dependsOn: new[] { "import.identities" })
        });

        var executor = CreateExecutor();
        var stateStore = new InMemoryStateStore();
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, stateStore, CancellationToken.None);

        // Assert
        // Since Identities is already Skipped at plan time, Nodes will be skipped during tier extraction
        Assert.IsTrue(result, "Import phase should succeed (no executed tasks failed)");

        var persistedJson = await stateStore.ReadAsync(".migration/Checkpoints/plan.json", CancellationToken.None);
        var persistedPlan = System.Text.Json.JsonSerializer.Deserialize<JobTaskList>(persistedJson!);
        var nodesTask = persistedPlan!.Tasks.First(t => t.Id == "import.nodes");

        Assert.AreEqual(JobTaskStatus.Skipped, nodesTask.Status, "Nodes task should be Skipped");
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_FailedTaskDoesNotCancelSiblings()
    {
        // Arrange
        var identitiesModule = new Mock<IModule>(MockBehavior.Loose);
        identitiesModule.SetupGet(m => m.Name).Returns("Identities");
        identitiesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var nodesModule = new Mock<IModule>(MockBehavior.Loose);
        nodesModule.SetupGet(m => m.Name).Returns("Nodes");
        nodesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Nodes failed"));

        var teamsModule = new Mock<IModule>(MockBehavior.Loose);
        teamsModule.SetupGet(m => m.Name).Returns("Teams");
        teamsModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identities"] = identitiesModule.Object,
            ["Nodes"] = nodesModule.Object,
            ["Teams"] = teamsModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import"),
            CreateTask("import.nodes", "Nodes Import", "Import"),
            CreateTask("import.teams", "Teams Import", "Import")
        });

        var executor = CreateExecutor();
        var stateStore = new InMemoryStateStore();
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, stateStore, CancellationToken.None);

        // Assert
        Assert.IsFalse(result, "Import phase should fail (Nodes failed)");

        var persistedJson = await stateStore.ReadAsync(".migration/Checkpoints/plan.json", CancellationToken.None);
        var persistedPlan = System.Text.Json.JsonSerializer.Deserialize<JobTaskList>(persistedJson!);

        var identitiesTask = persistedPlan!.Tasks.First(t => t.Id == "import.identities");
        var nodesTask = persistedPlan.Tasks.First(t => t.Id == "import.nodes");
        var teamsTask = persistedPlan.Tasks.First(t => t.Id == "import.teams");

        Assert.AreEqual(JobTaskStatus.Completed, identitiesTask.Status, "Identities should complete");
        Assert.AreEqual(JobTaskStatus.Failed, nodesTask.Status, "Nodes should fail");
        Assert.AreEqual(JobTaskStatus.Completed, teamsTask.Status, "Teams should complete (sibling of failed Nodes)");
    }

    [TestMethod]
    public async Task LoadOrResetAsync_RunningTaskResetToPending()
    {
        // Arrange
        var stateStore = new InMemoryStateStore();

        var plan = CreatePlan(new[]
        {
            CreateTask("export.identities", "Identities Export", "Export", status: JobTaskStatus.Running, startedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateTask("export.nodes", "Nodes Export", "Export", status: JobTaskStatus.Completed)
        });

        var json = System.Text.Json.JsonSerializer.Serialize(plan);
        await stateStore.WriteAsync(".migration/Checkpoints/plan.json", json, CancellationToken.None);

        // Act
        var loaded = await JobPlanExecutor.LoadOrResetAsync(stateStore, CancellationToken.None);

        // Assert
        Assert.IsNotNull(loaded, "Plan should be loaded");
        var identitiesTask = loaded.Tasks.First(t => t.Id == "export.identities");
        var nodesTask = loaded.Tasks.First(t => t.Id == "export.nodes");

        Assert.AreEqual(JobTaskStatus.Pending, identitiesTask.Status, "Running task should be reset to Pending");
        Assert.IsNull(identitiesTask.StartedAt, "StartedAt should be cleared");
        Assert.AreEqual(JobTaskStatus.Completed, nodesTask.Status, "Completed task should remain Completed");
    }

    [TestMethod]
    public async Task LoadOrResetAsync_CorruptFile_ReturnsNull()
    {
        // Arrange
        var stateStore = new InMemoryStateStore();
        await stateStore.WriteAsync(".migration/Checkpoints/plan.json", "{ invalid json }", CancellationToken.None);

        // Act
        var loaded = await JobPlanExecutor.LoadOrResetAsync(stateStore, CancellationToken.None);

        // Assert
        Assert.IsNull(loaded, "Corrupt plan file should return null");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JobPlanExecutor CreateExecutor()
    {
        var progressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object;
        return new JobPlanExecutor(progressSink, NullLogger<JobPlanExecutor>.Instance);
    }

    private static JobTaskList CreatePlan(IEnumerable<JobTask> tasks)
    {
        return new JobTaskList
        {
            Tasks = tasks.ToList().AsReadOnly(),
            PushedAt = DateTimeOffset.UtcNow
        };
    }

    private static JobTask CreateTask(
        string id,
        string name,
        string phase,
        JobTaskStatus status = JobTaskStatus.Pending,
        string? skipReason = null,
        IReadOnlyList<string>? dependsOn = null,
        DateTimeOffset? startedAt = null)
    {
        return new JobTask
        {
            Id = id,
            Name = name,
            Phase = phase,
            Status = status,
            SkipReason = skipReason,
            DependsOn = dependsOn,
            StartedAt = startedAt
        };
    }

    /// <summary>
    /// Simple in-memory state store for tests.
    /// </summary>
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
        {
            return Task.FromResult(_data.ContainsKey(key));
        }

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            _data.Remove(key);
            return Task.CompletedTask;
        }
    }
}
