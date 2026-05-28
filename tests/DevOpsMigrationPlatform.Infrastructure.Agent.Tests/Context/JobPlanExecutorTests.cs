// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using Microsoft.Extensions.Logging;
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

                    return TaskExecutionResult.Completed();
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

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var exportContext = new ExportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object
        };

        // Act
        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            baseInventoryContext: null,
            endpointsByUrl: null,
            exportContext, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Export phase should succeed");
        Assert.AreEqual(4, startTimes.Count, "All 4 tasks should have started");

        // Check that at least 3 tasks started within 500 ms of each other (concurrent execution)
        var minStart = startTimes.Min();
        var concurrentStarts = startTimes.Count(t => (t - minStart).TotalMilliseconds < 500);
        Assert.IsTrue(concurrentStarts >= 3, $"At least 3 tasks should start within 500ms, but only {concurrentStarts} did");
    }

    [TestMethod]
    public async Task ExecuteExportPhaseAsync_WithCaptureAndAnalysePrerequisites_RunsThemBeforeExport()
    {
        // Arrange
        var executionOrder = new List<string>();
        var lockObj = new object();

        var workItemsModule = new Mock<IModule>(MockBehavior.Loose);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("Capture");
                }

                return Task.FromResult(TaskExecutionResult.Completed());
            });
        workItemsModule.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("Export");
                }

                return Task.FromResult(TaskExecutionResult.Completed());
            });

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItems"] = workItemsModule.Object
        };

        var inventoryAnalyser = new Mock<IAnalyser>(MockBehavior.Loose);
        inventoryAnalyser.SetupGet(a => a.Name).Returns("Inventory");
        inventoryAnalyser.Setup(a => a.AnalyseAsync(It.IsAny<AnalyseContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("Analyse");
                }

                return Task.FromResult(TaskExecutionResult.Completed());
            });

        var analysers = new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inventory"] = inventoryAnalyser.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("capture.workitems.testorg.testproject", "WorkItems Capture", "Capture"),
            CreateTask(
                "analyse.inventory.testorg.testproject",
                "Inventory Analyse",
                "Analyse",
                dependsOn: new[] { "capture.workitems.testorg.testproject" }),
            CreateTask(
                "export.workitems.testorg.testproject",
                "WorkItems Export",
                "Export",
                dependsOn: new[] { "analyse.inventory.testorg.testproject" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var inventoryContext = CreateMinimalInventoryContext() with
        {
            SourceEndpoint = new OrganisationEndpoint { ResolvedUrl = "https://dev.azure.com/testorg", Type = "Simulated" },
            Project = "testproject"
        };
        var exportContext = new ExportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object
        };
        var endpointsByUrl = new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://dev.azure.com/testorg"] = new() { ResolvedUrl = "https://dev.azure.com/testorg", Type = "Simulated" }
        };

        // Act
        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            analysers,
            inventoryContext,
            endpointsByUrl,
            exportContext, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Export phase should succeed when prerequisite capture/analyse tasks succeed.");
        CollectionAssert.AreEqual(new[] { "Capture", "Analyse", "Export" }, executionOrder);
    }

    [TestMethod]
    public async Task ExecuteExportPhaseAsync_WhenInventoryMarkerExists_DoesNotSkipPrerequisitesInExecutor()
    {
        var executionOrder = new List<string>();
        var lockObj = new object();

        var workItemsModule = new Mock<IModule>(MockBehavior.Loose);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("Capture");
                }

                return Task.FromResult(TaskExecutionResult.Completed());
            });
        workItemsModule.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("Export");
                }

                return Task.FromResult(TaskExecutionResult.Completed());
            });

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItems"] = workItemsModule.Object
        };

        var inventoryAnalyser = new Mock<IAnalyser>(MockBehavior.Loose);
        inventoryAnalyser.SetupGet(a => a.Name).Returns("Inventory");
        inventoryAnalyser.Setup(a => a.AnalyseAsync(It.IsAny<AnalyseContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("Analyse");
                }

                return Task.FromResult(TaskExecutionResult.Completed());
            });

        var analysers = new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inventory"] = inventoryAnalyser.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("capture.workitems.testorg.testproject", "WorkItems Capture", "Capture"),
            CreateTask(
                "analyse.inventory.testorg.testproject",
                "Inventory Analyse",
                "Analyse",
                dependsOn: new[] { "capture.workitems.testorg.testproject" }),
            CreateTask(
                "export.workitems.testorg.testproject",
                "WorkItems Export",
                "Export",
                dependsOn: new[] { "analyse.inventory.testorg.testproject" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        await package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress(PackagePathTestHelper.InventoryCompleteFile)),
            new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"), writable: false)),
            CancellationToken.None);

        var inventoryJson = System.Text.Json.JsonSerializer.Serialize(new InventoryReport
        {
            Totals = new InventoryTotals { WorkItems = 5 },
            Organisations = new[]
            {
                new OrganisationInventory
                {
                    Url = "https://dev.azure.com/testorg",
                    Projects = new[]
                    {
                        new ProjectInventory { Name = "testproject", WorkItems = 5 }
                    }
                }
            }
        });

        await package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress("inventory.json")),
            new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(inventoryJson), writable: false)),
            CancellationToken.None);

        var inventoryContext = CreateMinimalInventoryContext(package) with
        {
            SourceEndpoint = new OrganisationEndpoint { ResolvedUrl = "https://dev.azure.com/testorg", Type = "Simulated" },
            Project = "testproject"
        };
        var exportContext = new ExportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object
        };
        var endpointsByUrl = new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://dev.azure.com/testorg"] = new() { ResolvedUrl = "https://dev.azure.com/testorg", Type = "Simulated" }
        };

        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            analysers,
            inventoryContext,
            endpointsByUrl,
            exportContext, CancellationToken.None);

        Assert.IsTrue(result);
        CollectionAssert.AreEqual(new[] { "Capture", "Analyse", "Export" }, executionOrder);

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        Assert.IsNotNull(persistedPlan);
        var executedCapture = persistedPlan.Tasks.First(t => t.Id == "capture.workitems.testorg.testproject");
        Assert.AreEqual(JobTaskStatus.Completed, executedCapture.Status);
        Assert.AreEqual(JobTaskStatus.Completed, persistedPlan.Tasks.First(t => t.Id == "analyse.inventory.testorg.testproject").Status);
        Assert.AreEqual(JobTaskStatus.Completed, persistedPlan.Tasks.First(t => t.Id == "export.workitems.testorg.testproject").Status);
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_WhenCaptureHandlerReportsSkipped_PersistsReportedStatusWithoutSynthesizingCompletion()
    {
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("workitems");
        captureHandler
            .Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskExecutionResult(
                JobTaskStatus.Skipped,
                "Inventory already completed for this package.",
                KnownTotal: 5,
                CompletedCount: 5));

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["workitems"] = captureHandler.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("capture.workitems.testorg.testproject", "WorkItems Capture", "Capture")
        });
        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);

        var result = await executor.ExecuteTasksAsync(
            plan,
            handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            CreateMinimalInventoryContext(),
            null,
            null,
            new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.IsTrue(result, "A handler-reported skipped/already-done state should be treated as successful execution.");

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        Assert.IsNotNull(persistedPlan);

        var captureTask = persistedPlan.Tasks.Single(t => t.Id == "capture.workitems.testorg.testproject");
        Assert.AreEqual(JobTaskStatus.Skipped, captureTask.Status);
        Assert.AreEqual("Inventory already completed for this package.", captureTask.SkipReason);
        Assert.AreEqual(5L, captureTask.KnownTotal);
        Assert.AreEqual(5L, captureTask.CompletedCount);
    }

    [TestMethod]
    public async Task ExecuteExportPhaseAsync_WhenLiveTaskCompletes_EmitsKnownTotalAndCompletedCount()
    {
        var progressEvents = new List<ProgressEvent>();

        var workItemsModule = new Mock<IModule>(MockBehavior.Loose);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule
            .Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItems"] = workItemsModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask(
                "export.workitems.testorg.testproject",
                "WorkItems Export",
                "Export",
                orgUrl: "https://dev.azure.com/testorg",
                projectName: "testproject")
        });

        var inventoryJson = System.Text.Json.JsonSerializer.Serialize(new InventoryReport
        {
            Totals = new InventoryTotals { WorkItems = 5 },
            Organisations = new[]
            {
                new OrganisationInventory
                {
                    Url = "https://dev.azure.com/testorg",
                    Projects = new[]
                    {
                        new ProjectInventory { Name = "testproject", WorkItems = 5 }
                    }
                }
            }
        });

        var progressSink = new Mock<IProgressSink>(MockBehavior.Loose);
        progressSink
            .Setup(sink => sink.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(evt => progressEvents.Add(evt));

        var package = PackageTestFactory.CreateLooseMock().Object;
        await package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress("inventory.json")),
            new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(inventoryJson), writable: false)),
            CancellationToken.None);
        var executor = CreateExecutor(progressSink: progressSink.Object, package: package);
        var exportContext = new ExportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = progressSink.Object
        };

        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            baseInventoryContext: null,
            endpointsByUrl: null,
            exportContext, CancellationToken.None);

        Assert.IsTrue(result);

        var completedEvent = progressEvents.Last(evt =>
            evt.TaskId == "export.workitems.testorg.testproject"
            && evt.TaskStatus == JobTaskStatus.Completed);

        Assert.AreEqual(5L, completedEvent.KnownTotal);
        Assert.AreEqual(5L, completedEvent.CompletedCount);

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        Assert.IsNotNull(persistedPlan);
        var completedTask = persistedPlan.Tasks.Single(t => t.Id == "export.workitems.testorg.testproject");
        Assert.AreEqual(5L, completedTask.KnownTotal);
        Assert.AreEqual(5L, completedTask.CompletedCount);
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

                return TaskExecutionResult.Completed();
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
                return Task.FromResult(TaskExecutionResult.Completed());
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

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, CancellationToken.None);

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
            .ReturnsAsync(TaskExecutionResult.Completed());

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

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, CancellationToken.None);

        // Assert
        Assert.IsFalse(result, "Import phase should fail");

        // Check that plan was persisted with correct statuses
        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        Assert.IsNotNull(persistedPlan, "Plan should be persisted");
        var identitiesTask = persistedPlan!.Tasks.First(t => t.Id == "import.identities");
        var workItemsTask = persistedPlan.Tasks.First(t => t.Id == "import.workitems");

        Assert.AreEqual(JobTaskStatus.Failed, identitiesTask.Status, "Identities task should be Failed");
        Assert.AreEqual(JobTaskStatus.Skipped, workItemsTask.Status, "WorkItems task should be Skipped");
        Assert.IsNotNull(workItemsTask.SkipReason, "WorkItems should have a skip reason");
        Assert.IsTrue(workItemsTask.SkipReason!.Contains("import.identities"), "Skip reason should mention the failed dependency");
    }

    [TestMethod]
    public async Task ExecuteExportPhaseAsync_PassesTaskIdIntoScopedExportContext()
    {
        ExportContext? observedContext = null;

        var workItemsModule = new Mock<IModule>(MockBehavior.Strict);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .Callback<ExportContext, CancellationToken>((context, _) => observedContext = context)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItems"] = workItemsModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask(
                "export.workitems.testorg.testproject",
                "WorkItems Export",
                "Export",
                projectName: "testproject")
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var exportContext = new ExportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object
        };

        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            baseInventoryContext: null,
            endpointsByUrl: null,
            exportContext, CancellationToken.None);

        Assert.IsTrue(result, "Export phase should succeed.");
        Assert.IsNotNull(observedContext, "The module should receive a scoped export context.");
        Assert.AreEqual("export.workitems.testorg.testproject", observedContext!.TaskId);
        Assert.AreEqual("testproject", observedContext.Project);
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_DisabledDependency_DependentSkipped()
    {
        // Arrange
        var nodesModule = new Mock<IModule>(MockBehavior.Loose);
        nodesModule.SetupGet(m => m.Name).Returns("Nodes");
        nodesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nodes"] = nodesModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import", status: JobTaskStatus.Skipped, skipReason: "Disabled"),
            CreateTask("import.nodes", "Nodes Import", "Import", dependsOn: new[] { "import.identities" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, CancellationToken.None);

        // Assert
        // Since Identities is already Skipped at plan time, Nodes will be skipped during tier extraction
        Assert.IsTrue(result, "Import phase should succeed (no executed tasks failed)");

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        Assert.IsNotNull(persistedPlan);
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
            .ReturnsAsync(TaskExecutionResult.Completed());

        var nodesModule = new Mock<IModule>(MockBehavior.Loose);
        nodesModule.SetupGet(m => m.Name).Returns("Nodes");
        nodesModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Nodes failed"));

        var teamsModule = new Mock<IModule>(MockBehavior.Loose);
        teamsModule.SetupGet(m => m.Name).Returns("Teams");
        teamsModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());

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

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(package: package);
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, CancellationToken.None);

        // Assert
        Assert.IsFalse(result, "Import phase should fail (Nodes failed)");

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        Assert.IsNotNull(persistedPlan);

        var identitiesTask = persistedPlan!.Tasks.First(t => t.Id == "import.identities");
        var nodesTask = persistedPlan.Tasks.First(t => t.Id == "import.nodes");
        var teamsTask = persistedPlan.Tasks.First(t => t.Id == "import.teams");

        Assert.AreEqual(JobTaskStatus.Completed, identitiesTask.Status, "Identities should complete");
        Assert.AreEqual(JobTaskStatus.Failed, nodesTask.Status, "Nodes should fail");
        Assert.AreEqual(JobTaskStatus.Completed, teamsTask.Status, "Teams should complete (sibling of failed Nodes)");
    }

    [TestMethod]
    public async Task LoadOrResetAsync_RunningTasksAreResetToPending()
    {
        // Arrange

        var plan = CreatePlan(new[]
        {
            CreateTask("export.identities", "Identities Export", "Export", status: JobTaskStatus.Running, startedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateTask("export.nodes", "Nodes Export", "Export", status: JobTaskStatus.Completed)
        });

        var json = System.Text.Json.JsonSerializer.Serialize(plan);

        // Act
        var package = PackageTestFactory.CreateLooseMock().Object;
        await package.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.ExecutionPlan),
            new PackageMetaPayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json), writable: false)),
            CancellationToken.None);
        var loaded = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);

        // Assert
        Assert.IsNotNull(loaded, "Plan should be loaded");
        var identitiesTask = loaded.Tasks.First(t => t.Id == "export.identities");
        var nodesTask = loaded.Tasks.First(t => t.Id == "export.nodes");

        Assert.AreEqual(JobTaskStatus.Pending, identitiesTask.Status, "Running task should be reset to Pending");
        Assert.IsNull(identitiesTask.StartedAt, "StartedAt should be cleared");
        Assert.AreEqual(JobTaskStatus.Completed, nodesTask.Status, "Completed task should remain Completed");
    }

    [TestMethod]
    public async Task LoadOrResetAsync_CorruptPlan_ReturnsNull()
    {
        // Arrange
        var package = PackageTestFactory.CreateLooseMock().Object;
        await package.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.ExecutionPlan),
            new PackageMetaPayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{ invalid json }"), writable: false)),
            CancellationToken.None);

        // Act
        var loaded = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);

        // Assert
        Assert.IsNull(loaded, "Corrupt plan file should return null");
    }

    /// <summary>
    /// Regression guard: if a previous Export run completed all tasks and the agent
    /// resumes without ForceFresh, no module's ExportAsync should be invoked.
    /// This covers the scenario "Completed tasks not re-executed on resume".
    /// </summary>
    [TestMethod]
    public async Task ExecuteExportPhaseAsync_AllTasksAlreadyCompleted_NoModuleCalled()
    {
        // Arrange
        var exportCalled = new List<string>();

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Identities", "Nodes", "Teams", "WorkItems" })
        {
            var module = new Mock<IModule>(MockBehavior.Strict);
            module.SetupGet(m => m.Name).Returns(name);
            // ExportAsync must NOT be set up — Strict mock will throw if called
            modules[name] = module.Object;
        }

        var plan = CreatePlan(new[]
        {
            CreateTask("export.identities", "Identities Export", "Export", status: JobTaskStatus.Completed),
            CreateTask("export.nodes",      "Nodes Export",      "Export", status: JobTaskStatus.Completed),
            CreateTask("export.teams",      "Teams Export",      "Export", status: JobTaskStatus.Completed),
            CreateTask("export.workitems",  "WorkItems Export",  "Export", status: JobTaskStatus.Completed)
        });

        var executor = CreateExecutor();
        var exportContext = new ExportContext();

        // Act
        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            baseInventoryContext: null,
            endpointsByUrl: null,
            exportContext, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Export phase should return true when all tasks are already completed");
        Assert.AreEqual(0, exportCalled.Count, "No module should have been executed");
    }

    /// <summary>
    /// Regression guard: same as the Export variant but for the Import path.
    /// </summary>
    [TestMethod]
    public async Task ExecuteImportPhaseAsync_AllTasksAlreadyCompleted_NoModuleCalled()
    {
        // Arrange
        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Identities", "Nodes", "Teams", "WorkItems" })
        {
            var module = new Mock<IModule>(MockBehavior.Strict);
            module.SetupGet(m => m.Name).Returns(name);
            // ImportAsync must NOT be set up — Strict mock will throw if called
            modules[name] = module.Object;
        }

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import", status: JobTaskStatus.Completed),
            CreateTask("import.nodes",      "Nodes Import",      "Import", status: JobTaskStatus.Completed),
            CreateTask("import.teams",      "Teams Import",      "Import", status: JobTaskStatus.Completed),
            CreateTask("import.workitems",  "WorkItems Import",  "Import", status: JobTaskStatus.Completed)
        });

        var executor = CreateExecutor();
        var importContext = new ImportContext();

        // Act
        var result = await executor.ExecuteImportPhaseAsync(plan, modules, importContext, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Import phase should return true when all tasks are already completed");
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_PartialResume_CompletedDependencyAllowsDependentTaskToRun()
    {
        var identitiesModule = new Mock<IModule>(MockBehavior.Strict);
        identitiesModule.SetupGet(m => m.Name).Returns("Identities");

        var invoked = false;
        var workItemsModule = new Mock<IModule>(MockBehavior.Strict);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identities"] = identitiesModule.Object,
            ["WorkItems"] = workItemsModule.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import", status: JobTaskStatus.Completed),
            CreateTask("import.workitems", "WorkItems Import", "Import", dependsOn: new[] { "import.identities" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var result = await CreateExecutor(package: package).ExecuteImportPhaseAsync(
            plan,
            modules,
            new ImportContext { Package = package, ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object, Job = new Job { JobId = "test-job" } },
            CancellationToken.None);

        Assert.IsTrue(result, "A completed dependency should satisfy the dependent import task on resume.");
        Assert.IsTrue(invoked, "The dependent pending task should execute after its dependency has already completed.");
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_FailedDependencyOnResume_ReturnsFalseAndSkipsDependentTask()
    {
        var invoked = false;
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("workitems");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["workitems"] = captureHandler.Object
        };
        var plan = CreatePlan(new[]
        {
            CreateTask("capture.identities.org.project", "Identities Capture", "Capture", status: JobTaskStatus.Failed),
            CreateTask(
                "capture.workitems.org.project",
                "WorkItems Capture",
                "Capture",
                dependsOn: new[] { "capture.identities.org.project" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var result = await CreateExecutor(package: package).ExecuteTasksAsync(
            plan,
            handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            CreateMinimalInventoryContext(),
            null,
            null,
            new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.IsFalse(result, "A resumed canonical plan containing a failed task must keep the job failed.");
        Assert.IsFalse(invoked, "Dependent capture handlers must not run when their dependency already failed.");

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        var persisted = persistedPlan is null ? null : System.Text.Json.JsonSerializer.Serialize(persistedPlan);
        Assert.IsNotNull(persisted, "The skipped dependent state should be persisted for resume and UI bootstrap.");
        StringAssert.Contains(persisted, "capture.workitems.org.project");
        StringAssert.Contains(persisted, "failed or was skipped");
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_SkippedDependency_SkipsDependentTaskWithoutInvokingHandler()
    {
        var invoked = false;
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("workitems");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["workitems"] = captureHandler.Object
        };
        var plan = CreatePlan(new[]
        {
            CreateTask("capture.identities.org.project", "Identities Capture", "Capture", status: JobTaskStatus.Skipped),
            CreateTask(
                "capture.workitems.org.project",
                "WorkItems Capture",
                "Capture",
                dependsOn: new[] { "capture.identities.org.project" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var result = await CreateExecutor(package: package).ExecuteTasksAsync(
            plan,
            handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            CreateMinimalInventoryContext(),
            null,
            null,
            new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.IsTrue(result, "Skipping a blocked dependent task is a successful no-op when no runnable tasks fail.");
        Assert.IsFalse(invoked, "Dependent capture handlers must not run when their dependency was skipped.");

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        var persisted = persistedPlan is null ? null : System.Text.Json.JsonSerializer.Serialize(persistedPlan);
        Assert.IsNotNull(persisted, "The skipped dependent state should be persisted for resume and UI bootstrap.");
        StringAssert.Contains(persisted, "capture.workitems.org.project");
        StringAssert.Contains(persisted, "failed or was skipped");
    }

    [TestMethod]
    public async Task ExecuteImportPhaseAsync_FailedDependencyOnResume_ReturnsFalseAndSkipsDependentTask()
    {
        var invoked = false;
        var workItemsModule = new Mock<IModule>(MockBehavior.Strict);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItems"] = workItemsModule.Object
        };
        var plan = CreatePlan(new[]
        {
            CreateTask("import.identities", "Identities Import", "Import", status: JobTaskStatus.Failed),
            CreateTask("import.workitems", "WorkItems Import", "Import", dependsOn: new[] { "import.identities" })
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var result = await CreateExecutor(package: package).ExecuteImportPhaseAsync(
            plan,
            modules,
            new ImportContext(), CancellationToken.None);

        Assert.IsFalse(result, "A resumed import plan containing a failed task must keep the phase failed.");
        Assert.IsFalse(invoked, "Dependent import modules must not run when their dependency already failed.");

        var persistedPlan = await JobPlanExecutor.LoadOrResetAsync(package, CancellationToken.None);
        var persisted = persistedPlan is null ? null : System.Text.Json.JsonSerializer.Serialize(persistedPlan);
        Assert.IsNotNull(persisted, "The skipped dependent state should be persisted for resume and UI bootstrap.");
        StringAssert.Contains(persisted, "import.workitems");
        StringAssert.Contains(persisted, "failed or was skipped");
    }

    /// <summary>
    /// Regression guard: mixed plan where some tasks are Completed and one is still Pending.
    /// Only the Pending task's module should execute; the completed ones must not.
    /// </summary>
    [TestMethod]
    public async Task ExecuteExportPhaseAsync_PartialResume_OnlyPendingTaskExecuted()
    {
        // Arrange
        var exportCalled = new List<string>();
        var lockObj = new object();

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Identities", "Nodes", "Teams" })
        {
            var module = new Mock<IModule>(MockBehavior.Strict);
            module.SetupGet(m => m.Name).Returns(name);
            // ExportAsync is NOT set up — Strict mock will throw if called
            modules[name] = module.Object;
        }

        // WorkItems is the only pending task — its module MUST be called
        var workItemsModule = new Mock<IModule>(MockBehavior.Loose);
        workItemsModule.SetupGet(m => m.Name).Returns("WorkItems");
        workItemsModule.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                lock (lockObj) { exportCalled.Add("WorkItems"); }
                return Task.FromResult(TaskExecutionResult.Completed());
            });
        modules["WorkItems"] = workItemsModule.Object;

        var plan = CreatePlan(new[]
        {
            CreateTask("export.identities", "Identities Export", "Export", status: JobTaskStatus.Completed),
            CreateTask("export.nodes",      "Nodes Export",      "Export", status: JobTaskStatus.Completed),
            CreateTask("export.teams",      "Teams Export",      "Export", status: JobTaskStatus.Completed),
            CreateTask("export.workitems",  "WorkItems Export",  "Export", status: JobTaskStatus.Pending)
        });

        var executor = CreateExecutor();
        var exportContext = new ExportContext();

        // Act
        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            baseInventoryContext: null,
            endpointsByUrl: null,
            exportContext, CancellationToken.None);

        // Assert
        Assert.IsTrue(result, "Export phase should succeed");
        Assert.AreEqual(1, exportCalled.Count, "Only the pending WorkItems task should execute");
        Assert.AreEqual("WorkItems", exportCalled[0]);
    }

    // ── T013: GetModuleName extraction for capture tasks ──────────────────────

    /// <summary>
    /// "capture.workitems.org.project" must route to the handler named "workitems".
    /// Validates GetModuleName extracts the second dot-segment (index 1) correctly.
    /// </summary>
    [TestMethod]
    public async Task ExecuteTasksAsync_CaptureTask_WorkitemsId_RoutesToWorkitemsHandler()
    {
        var invoked = false;
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("workitems");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["workitems"] = captureHandler.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("capture.workitems.testorg.testproject", "WorkItems Capture", "Capture")
        });

        var executor = CreateExecutor();
        var ctx = CreateMinimalInventoryContext();

        var result = await executor.ExecuteTasksAsync(
            plan, handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            ctx, null, null, null, CancellationToken.None);

        Assert.IsTrue(result, "ExecuteTasksAsync should succeed when handler is found");
        Assert.IsTrue(invoked, "Handler named 'workitems' must be invoked for task 'capture.workitems.org.project'");
    }

    /// <summary>
    /// "capture.dependencies.org.project" must route to the handler named "dependencies".
    /// Validates GetModuleName extracts the second dot-segment (index 1) correctly.
    /// </summary>
    [TestMethod]
    public async Task ExecuteTasksAsync_CaptureTask_DependenciesId_RoutesToDependenciesHandler()
    {
        var invoked = false;
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("dependencies");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencies"] = captureHandler.Object
        };

        var plan = CreatePlan(new[]
        {
            CreateTask("capture.dependencies.testorg.testproject", "Dependencies Capture", "Capture")
        });

        var executor = CreateExecutor();
        var ctx = CreateMinimalInventoryContext();

        var result = await executor.ExecuteTasksAsync(
            plan, handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            ctx, null, null, null, CancellationToken.None);

        Assert.IsTrue(result, "ExecuteTasksAsync should succeed when handler is found");
        Assert.IsTrue(invoked, "Handler named 'dependencies' must be invoked for task 'capture.dependencies.org.project'");
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_CaptureTask_ExposesResolvedSourceEndpointThroughAccessor_AndRestoresPreviousSource()
    {
        var endpointAccessor = new CurrentJobEndpointAccessor();
        endpointAccessor.SetSource(new TestSourceEndpointInfo(
            "https://original.example",
            "OriginalProject",
            "OriginalConnector",
            new OrganisationEndpoint
            {
                ResolvedUrl = "https://original.example",
                Type = "OriginalConnector"
            }));

        var activeSourceEndpoint = new ActiveJobSourceEndpointInfo(endpointAccessor);
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("dependencies");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.AreEqual("https://resolved.example", activeSourceEndpoint.Url);
                Assert.AreEqual("TestProject", activeSourceEndpoint.Project);
                Assert.AreEqual("AzureDevOpsServices", activeSourceEndpoint.ConnectorType);
                Assert.AreEqual(AuthenticationType.AccessToken, activeSourceEndpoint.ToOrganisationEndpoint().Authentication.Type);
                Assert.AreEqual("secret-token", activeSourceEndpoint.ToOrganisationEndpoint().Authentication.ResolvedAccessToken);
            })
            .ReturnsAsync(TaskExecutionResult.Completed());

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencies"] = captureHandler.Object
        };

        var plan = CreatePlan(new[]
        {
            new JobTask
            {
                Id = "capture.dependencies.testorg.testproject",
                Name = "Dependencies Capture",
                Phase = "Capture",
                TaskKind = TaskKind.Capture,
                Status = JobTaskStatus.Pending,
                OrganisationUrl = "https://resolved.example",
                ProjectName = "TestProject"
            }
        });

        var endpointsByUrl = new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://resolved.example"] = new()
            {
                ResolvedUrl = "https://resolved.example",
                Type = "AzureDevOpsServices",
                ApiVersion = "7.1",
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = AuthenticationType.AccessToken,
                    ResolvedAccessToken = "secret-token"
                }
            }
        };

        var executor = CreateExecutor(endpointAccessor);
        var ctx = CreateMinimalInventoryContext();

        var result = await executor.ExecuteTasksAsync(
            plan,
            handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            ctx,
            null,
            null,
            endpointsByUrl, CancellationToken.None);

        Assert.IsTrue(result, "ExecuteTasksAsync should succeed when the capture handler is present.");
        Assert.AreEqual("https://original.example", activeSourceEndpoint.Url);
        Assert.AreEqual("OriginalProject", activeSourceEndpoint.Project);
        Assert.AreEqual("OriginalConnector", activeSourceEndpoint.ConnectorType);
    }

    [TestMethod]
    public async Task ExecuteExportPhaseAsync_ExportTask_ExposesTaskProjectThroughAccessor_WhenFallbackSourceHasNoUrl()
    {
        var endpointAccessor = new CurrentJobEndpointAccessor();
        endpointAccessor.SetSource(new TestSourceEndpointInfo(
            string.Empty,
            string.Empty,
            "Simulated",
            new OrganisationEndpoint
            {
                ResolvedUrl = string.Empty,
                Type = "Simulated"
            }));

        var activeSourceEndpoint = new ActiveJobSourceEndpointInfo(endpointAccessor);
        var module = new Mock<IModule>(MockBehavior.Strict);
        module.SetupGet(m => m.Name).Returns("Identities");
        module.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.AreEqual(string.Empty, activeSourceEndpoint.Url);
                Assert.AreEqual("RoundtripProject", activeSourceEndpoint.Project);
                Assert.AreEqual("Simulated", activeSourceEndpoint.ConnectorType);
            })
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identities"] = module.Object
        };

        var plan = CreatePlan(new[]
        {
            new JobTask
            {
                Id = "export.identities.unknown.roundtripproject",
                Name = "Identities Export",
                Phase = "Export",
                TaskKind = TaskKind.Export,
                Status = JobTaskStatus.Pending,
                ProjectName = "RoundtripProject"
            }
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(endpointAccessor, package: package);
        var exportContext = new ExportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object
        };

        var result = await executor.ExecuteExportPhaseAsync(
            plan,
            modules,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            baseInventoryContext: null,
            endpointsByUrl: null,
            exportContext, CancellationToken.None);

        Assert.IsTrue(result, "Export phase should succeed when fallback source context has no URL.");
        Assert.AreEqual(string.Empty, activeSourceEndpoint.Url);
        Assert.AreEqual(string.Empty, activeSourceEndpoint.Project);
        Assert.AreEqual("Simulated", activeSourceEndpoint.ConnectorType);
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_ImportTask_ExposesTaskProjectThroughTargetAccessor_WhenFallbackTargetHasNoUrl()
    {
        var endpointAccessor = new CurrentJobEndpointAccessor();
        endpointAccessor.SetTarget(new TestTargetEndpointInfo(
            string.Empty,
            string.Empty,
            "Simulated",
            new OrganisationEndpoint
            {
                ResolvedUrl = string.Empty,
                Type = "Simulated"
            }));

        var activeTargetEndpoint = new ActiveJobTargetEndpointInfo(endpointAccessor);
        var module = new Mock<IModule>(MockBehavior.Strict);
        module.SetupGet(m => m.Name).Returns("Nodes");
        module.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.AreEqual(string.Empty, activeTargetEndpoint.Url);
                Assert.AreEqual("RoundtripProject", activeTargetEndpoint.Project);
                Assert.AreEqual("Simulated", activeTargetEndpoint.ConnectorType);
            })
            .ReturnsAsync(TaskExecutionResult.Completed());

        var modules = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nodes"] = module.Object
        };

        var plan = CreatePlan(new[]
        {
            new JobTask
            {
                Id = "import.nodes.unknown.roundtripproject",
                Name = "Nodes Import",
                Phase = "Import",
                TaskKind = TaskKind.Import,
                Status = JobTaskStatus.Pending,
                ProjectName = "RoundtripProject"
            }
        });

        var package = PackageTestFactory.CreateLooseMock().Object;
        var executor = CreateExecutor(endpointAccessor, package: package);
        var importContext = new ImportContext
        {
            Job = new Job { JobId = "test-job" },
            Package = package,
            ProgressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object
        };

        var result = await executor.ExecuteImportPhaseAsync(
            plan,
            modules,
            importContext, CancellationToken.None);

        Assert.IsTrue(result, "Import execution should succeed when fallback target context has no URL.");
        Assert.AreEqual(string.Empty, activeTargetEndpoint.Url);
    }

    /// <summary>
    /// When no capture handler matches the task's module name, the executor must log
    /// an Error with {TaskId} and {HandlerName} structured parameters and fail the task/job.
    /// </summary>
    [TestMethod]
    public async Task ExecuteTasksAsync_CaptureTask_NoMatchingHandler_LogsErrorAndFailsTask()
    {
        var mockLogger = new Mock<ILogger<JobPlanExecutor>>(MockBehavior.Loose);
        var executor = CreateExecutorWithLogger(mockLogger.Object);

        var plan = CreatePlan(new[]
        {
            CreateTask("capture.dependencies.testorg.testproject", "Dependencies Capture", "Capture")
        });

        var result = await executor.ExecuteTasksAsync(
            plan,
            new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            null, null, null, null, CancellationToken.None);

        Assert.IsFalse(result, "ExecuteTasksAsync should fail when a handler is missing");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => LogStateHasTaskIdAndHandlerName(v)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "LogError must be called exactly once with {TaskId} and {HandlerName} structured parameters");
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_CaptureTask_UnknownOrganisationUrl_FailsWithoutInvokingHandler()
    {
        var invoked = false;
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("dependencies");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => invoked = true)
            .ReturnsAsync(TaskExecutionResult.Completed());

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencies"] = captureHandler.Object
        };

        var plan = CreatePlan(new[]
        {
            new JobTask
            {
                Id = "capture.dependencies.testorg.testproject",
                Name = "Dependencies Capture",
                Phase = "Capture",
                TaskKind = TaskKind.Capture,
                Status = JobTaskStatus.Pending,
                OrganisationUrl = "https://missing.example",
                ProjectName = "TestProject"
            }
        });

        var executor = CreateExecutor();
        var ctx = CreateMinimalInventoryContext();

        var result = await executor.ExecuteTasksAsync(
            plan,
            handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            ctx,
            null,
            null,
            new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.IsFalse(result, "ExecuteTasksAsync should fail when the capture task organisation URL cannot be resolved.");
        Assert.IsFalse(invoked, "Capture handler must not be invoked when the task organisation URL cannot be resolved.");
    }

    [TestMethod]
    public async Task ExecuteTasksAsync_WithPackageBoundary_PersistsPlanViaPackageMeta()
    {
        var captureHandler = new Mock<ICapture>(MockBehavior.Strict);
        captureHandler.SetupGet(c => c.Name).Returns("dependencies");
        captureHandler.Setup(c => c.CaptureAsync(It.IsAny<InventoryContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var handlers = new Dictionary<string, ICapture>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencies"] = captureHandler.Object
        };

        var plan = CreatePlan(new[]
        {
            new JobTask
            {
                Id = "capture.dependencies.testorg.testproject",
                Name = "Dependencies Capture",
                Phase = "Capture",
                TaskKind = TaskKind.Capture,
                Status = JobTaskStatus.Pending,
                OrganisationUrl = "https://dev.azure.com/testorg",
                ProjectName = "TestProject"
            }
        });

        var executor = CreateExecutor(package: package.Object);
        var ctx = CreateMinimalInventoryContext();
        var endpoints = new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://dev.azure.com/testorg"] = new OrganisationEndpoint
            {
                ResolvedUrl = "https://dev.azure.com/testorg",
                Type = "Simulated"
            }
        };

        var result = await executor.ExecuteTasksAsync(
            plan,
            handlers,
            new Dictionary<string, IAnalyser>(StringComparer.OrdinalIgnoreCase),
            ctx,
            null,
            null,
            endpoints, CancellationToken.None);

        Assert.IsTrue(result, "Capture task should complete successfully.");
        package.Verify(p => p.PersistMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
            It.IsAny<PackageMetaPayload>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static bool LogStateHasTaskIdAndHandlerName(object v)
    {
        var state = v as IReadOnlyList<KeyValuePair<string, object?>>;
        return state != null
            && state.Any(kv => kv.Key == "TaskId" && kv.Value?.ToString() == "capture.dependencies.testorg.testproject")
            && state.Any(kv => kv.Key == "HandlerName" && kv.Value?.ToString() == "dependencies");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JobPlanExecutor CreateExecutor(
        ICurrentJobEndpointAccessor? endpointAccessor = null,
        IProgressSink? progressSink = null,
        IPackageAccess? package = null)
    {
        progressSink ??= new Mock<IProgressSink>(MockBehavior.Loose).Object;
        package ??= PackageTestFactory.CreateLooseMock().Object;
        return new JobPlanExecutor(progressSink, NullLogger<JobPlanExecutor>.Instance, endpointAccessor, package);
    }

    private static JobPlanExecutor CreateExecutorWithLogger(ILogger<JobPlanExecutor> logger)
    {
        var progressSink = new Mock<IProgressSink>(MockBehavior.Loose).Object;
        return new JobPlanExecutor(progressSink, logger, package: PackageTestFactory.CreateLooseMock().Object);
    }

    private static InventoryContext CreateMinimalInventoryContext(IPackageAccess? package = null)
        => new()
        {
            Job = new Job { JobId = "test-job" },
            Package = package ?? PackageTestFactory.CreateLooseMock().Object,
            SourceEndpoint = new OrganisationEndpoint(),
            Project = string.Empty,
            Organisations = Array.AsReadOnly(Array.Empty<ScopedOrganisationEndpoint>()),
            Policies = new JobPolicies()
        };

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
        DateTimeOffset? startedAt = null,
        string? orgUrl = null,
        string? projectName = null)
    {
        var kind = phase.ToUpperInvariant() switch
        {
            "EXPORT" => TaskKind.Export,
            "IMPORT" => TaskKind.Import,
            "CAPTURE" => TaskKind.Capture,
            "ANALYSE" => TaskKind.Analyse,
            _ => TaskKind.Export // safe default for legacy test tasks
        };

        return new JobTask
        {
            Id = id,
            Name = name,
            Phase = phase,
            TaskKind = kind,
            Status = status,
            SkipReason = skipReason,
            DependsOn = dependsOn,
            StartedAt = startedAt,
            OrganisationUrl = orgUrl,
            ProjectName = projectName
        };
    }

    private sealed record TestSourceEndpointInfo(string Url, string Project, string ConnectorType, OrganisationEndpoint Endpoint) : ISourceEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => Endpoint;
    }

    private sealed record TestTargetEndpointInfo(string Url, string Project, string ConnectorType, OrganisationEndpoint Endpoint) : ITargetEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => Endpoint;
    }
}
