// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobExecutionPlanBuilderDependsOnTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_ImportPhase_WorkItemsDependsOnIdentitiesAndNodes()
    {
        // Arrange
        var workItemsModule = CreateModule("WorkItems", new[]
        {
            new ModuleDependency(typeof(FakeIdentitiesModule), DependencyPhase.Import) { ModuleNameOverride = "Identities" },
            new ModuleDependency(typeof(FakeNodesModule), DependencyPhase.Import) { ModuleNameOverride = "Nodes" }
        });
        var identitiesModule = CreateModule("Identities", Array.Empty<ModuleDependency>());
        var nodesModule = CreateModule("Nodes", Array.Empty<ModuleDependency>());
        var teamsModule = CreateModule("Teams", Array.Empty<ModuleDependency>());

        var builder = CreateBuilder(new[] { workItemsModule, identitiesModule, nodesModule, teamsModule });
        var config = AllEnabledConfig();
        var package = PackageTestFactory.CreateLooseMock().Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Import, package, CancellationToken.None);

        // Assert
        var workItemsTask = plan.Tasks.First(t => t.Id.StartsWith("import.workitems"));
        Assert.IsNotNull(workItemsTask.DependsOn, "WorkItems task should have dependencies");
        Assert.IsTrue(workItemsTask.DependsOn.Any(d => d.StartsWith("import.identities")),
            "WorkItems should depend on Identities");
        Assert.IsTrue(workItemsTask.DependsOn.Any(d => d.StartsWith("import.nodes")),
            "WorkItems should depend on Nodes");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_ExportPhase_NoDependsOn()
    {
        // Arrange
        var workItemsModule = CreateModule("WorkItems", new[]
        {
            new ModuleDependency(typeof(FakeIdentitiesModule), DependencyPhase.Import) { ModuleNameOverride = "Identities" },
            new ModuleDependency(typeof(FakeNodesModule), DependencyPhase.Import) { ModuleNameOverride = "Nodes" }
        });
        var identitiesModule = CreateModule("Identities", Array.Empty<ModuleDependency>());
        var nodesModule = CreateModule("Nodes", Array.Empty<ModuleDependency>());

        var builder = CreateBuilder(new[] { workItemsModule, identitiesModule, nodesModule });
        var config = AllEnabledConfig();
        var package = PackageTestFactory.CreateLooseMock().Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Export, package, CancellationToken.None);

        // Assert
        foreach (var task in plan.Tasks.Where(t => t.Phase == "Export"))
        {
            Assert.IsTrue(task.DependsOn == null || task.DependsOn.Count == 0,
                $"Export task {task.Id} should have no dependencies");
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_ExportPhase_InventoryPrerequisiteContract_HoldsAcrossMarkerVariants()
    {
        // Arrange shared prerequisites
        var workItemsModule = CreateModule("WorkItems", new[]
        {
            new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Analyse)
        }, supportsInventory: true);

        var identitiesModule = CreateModule("Identities", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var nodesModule = CreateModule("Nodes", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var teamsModule = CreateModule("Teams", Array.Empty<ModuleDependency>(), supportsInventory: true);

        var builder = CreateBuilder(
            new[] { workItemsModule, identitiesModule, nodesModule, teamsModule },
            new[] { CreateAnalyser("Inventory", new[]
            {
                new ModuleDependency(typeof(FakeIdentitiesModule), DependencyPhase.Inventory) { ModuleNameOverride = "Identities" },
                new ModuleDependency(typeof(FakeNodesModule), DependencyPhase.Inventory) { ModuleNameOverride = "Nodes" },
                new ModuleDependency(typeof(FakeTeamsModule), DependencyPhase.Inventory) { ModuleNameOverride = "Teams" },
                new ModuleDependency(typeof(FakeWorkItemsModule), DependencyPhase.Inventory) { ModuleNameOverride = "WorkItems" }
            }) });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Url"] = "https://dev.azure.com/testorg",
                ["MigrationPlatform:Source:Project"] = "testproject",
                ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
                ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
                ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
                ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
            })
            .Build();

        var markerAbsentPackage = PackageTestFactory.CreateLooseMock().Object;
        var markerPresentPackage = PackageTestFactory.CreateLooseMock().Object;
        await markerPresentPackage.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.InventoryCompletionMarker),
            new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{}"), writable: false)),
            CancellationToken.None);

        // Act
        var markerAbsentPlan = await builder.BuildPlanAsync(config, JobKind.Export, markerAbsentPackage, CancellationToken.None);
        var markerPresentPlan = await builder.BuildPlanAsync(config, JobKind.Export, markerPresentPackage, CancellationToken.None);

        // Assert
        var absentIds = markerAbsentPlan.Tasks.Select(t => t.Id).ToList();
        var presentIds = markerPresentPlan.Tasks.Select(t => t.Id).ToList();

        CollectionAssert.Contains(absentIds, "analyse.inventory.testorg.testproject");
        CollectionAssert.Contains(presentIds, "analyse.inventory.testorg.testproject");
        CollectionAssert.Contains(absentIds, "capture.workitems.testorg.testproject");
        CollectionAssert.Contains(presentIds, "capture.workitems.testorg.testproject");

        var absentExport = markerAbsentPlan.Tasks.First(t => t.Id == "export.workitems.testorg.testproject");
        var presentExport = markerPresentPlan.Tasks.First(t => t.Id == "export.workitems.testorg.testproject");
        Assert.IsNotNull(absentExport.DependsOn);
        Assert.IsNotNull(presentExport.DependsOn);
        Assert.IsTrue(absentExport.DependsOn.Contains("analyse.inventory.testorg.testproject"));
        Assert.IsTrue(presentExport.DependsOn.Contains("analyse.inventory.testorg.testproject"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_CircularDependency_ThrowsInvalidOperationException()
    {
        // Arrange
        var moduleA = CreateModule("ModuleA", new[] { new ModuleDependency(typeof(FakeModuleB), DependencyPhase.Import) { ModuleNameOverride = "ModuleB" } });
        var moduleB = CreateModule("ModuleB", new[] { new ModuleDependency(typeof(FakeModuleA), DependencyPhase.Import) { ModuleNameOverride = "ModuleA" } });

        var builder = CreateBuilder(new[] { moduleA, moduleB });
        var config = AllEnabledConfig();
        var package = PackageTestFactory.CreateLooseMock().Object;

        // Act & Assert
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => builder.BuildPlanAsync(config, JobKind.Import, package, CancellationToken.None));

        Assert.IsTrue(exception.Message.Contains("Circular dependency"),
            "Exception message should mention circular dependency");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_DependencyOnDisabledModule_DependencyOmittedFromTask()
    {
        // Arrange
        var moduleB = CreateModule("ModuleB", new[] { new ModuleDependency(typeof(FakeModuleA), DependencyPhase.Import) { ModuleNameOverride = "ModuleA" } });
        var moduleA = CreateModule("ModuleA", Array.Empty<ModuleDependency>());

        var builder = CreateBuilder(new[] { moduleB, moduleA });

        // ModuleA is disabled
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Modules:ModuleA:Enabled"] = "false",
                ["MigrationPlatform:Modules:ModuleB:Enabled"] = "true"
            })
            .Build();

        var package = PackageTestFactory.CreateLooseMock().Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Import, package, CancellationToken.None);

        // Assert
        var taskB = plan.Tasks.FirstOrDefault(t => t.Id.StartsWith("import.moduleb"));
        Assert.IsNotNull(taskB, "ModuleB task should exist");

        // Since ModuleA is disabled, it won't have a task, so the dependency should be omitted
        Assert.IsTrue(taskB.DependsOn == null || !taskB.DependsOn.Any(d => d.StartsWith("import.modulea")),
            "ModuleB should not depend on disabled ModuleA");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_DependencyOnNonExistentModule_LogsWarningAndOmitsDependency()
    {
        // Arrange
        var moduleA = CreateModule("ModuleA", new[] { new ModuleDependency(typeof(FakeNonExistentModule), DependencyPhase.Import) { ModuleNameOverride = "NonExistentModule" } });
        var logger = new CapturingLogger<JobExecutionPlanBuilder>();

        var builder = CreateBuilder(new[] { moduleA }, logger: logger);
        var config = AllEnabledConfig();
        var package = PackageTestFactory.CreateLooseMock().Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Import, package, CancellationToken.None);

        // Assert
        var taskA = plan.Tasks.First(t => t.Id.StartsWith("import.modulea"));
        Assert.IsTrue(taskA.DependsOn == null || !taskA.DependsOn.Any(d => d.StartsWith("import.nonexistentmodule")),
            "Dependency on non-existent module should be omitted");

        var warning = logger.Entries.FirstOrDefault(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("ModuleA", StringComparison.Ordinal)
            && e.Message.Contains("NonExistentModule", StringComparison.Ordinal));
        Assert.IsFalse(string.IsNullOrWhiteSpace(warning.Message),
            "Expected warning log containing module and missing dependency names");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_PrepareKind_AddsAnalyseTasksFromPrepareDependencies()
    {
        // Arrange
        var moduleA = CreateModule(
            "ModuleA",
            new[] { new ModuleDependency(typeof(FakeInventoryAnalyser), DependencyPhase.Analyse) { ModuleNameOverride = "Inventory" } },
            supportsPrepare: true);
        var moduleB = CreateModule(
            "ModuleB",
            new[] { new ModuleDependency(typeof(FakeDependenciesAnalyser), DependencyPhase.Analyse) { ModuleNameOverride = "Dependencies" } },
            supportsPrepare: true);

        var builder = CreateBuilder(
            new[] { moduleA, moduleB },
            new[]
            {
                CreateAnalyser("Inventory", Array.Empty<ModuleDependency>()),
                CreateAnalyser("Dependencies", Array.Empty<ModuleDependency>())
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Modules:ModuleA:Enabled"] = "true",
                ["MigrationPlatform:Modules:ModuleB:Enabled"] = "true"
            })
            .Build();

        // Act
        var plan = await builder.BuildPlanAsync(
            config,
            JobKind.Prepare,
            PackageTestFactory.CreateLooseMock().Object,
            CancellationToken.None);

        // Assert
        var analyseTasks = plan.Tasks.Where(t => t.TaskKind == TaskKind.Analyse).ToList();
        CollectionAssert.AreEquivalent(
            new[] { "analyse.inventory", "analyse.dependencies" },
            analyseTasks.Select(t => t.Id).ToList());

        var prepareTasks = plan.Tasks.Where(t => t.TaskKind == TaskKind.Prepare).ToList();
        CollectionAssert.AreEquivalent(
            new[] { "prepare.modulea", "prepare.moduleb" },
            prepareTasks.Select(t => t.Id).ToList());
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_PrepareKind_PrepareTasksDependOnAnalyseTasks()
    {
        // Arrange
        var module = CreateModule(
            "ModuleA",
            new[]
            {
                new ModuleDependency(typeof(FakeInventoryAnalyser), DependencyPhase.Analyse) { ModuleNameOverride = "Inventory" },
                new ModuleDependency(typeof(FakeDependenciesAnalyser), DependencyPhase.Analyse) { ModuleNameOverride = "Dependencies" }
            },
            supportsPrepare: true);

        var builder = CreateBuilder(
            new[] { module },
            new[]
            {
                CreateAnalyser("Inventory", Array.Empty<ModuleDependency>()),
                CreateAnalyser("Dependencies", Array.Empty<ModuleDependency>())
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Modules:ModuleA:Enabled"] = "true"
            })
            .Build();

        // Act
        var plan = await builder.BuildPlanAsync(
            config,
            JobKind.Prepare,
            PackageTestFactory.CreateLooseMock().Object,
            CancellationToken.None);

        // Assert
        var prepareTask = plan.Tasks.Single(t => t.Id == "prepare.modulea");
        Assert.IsNotNull(prepareTask.DependsOn);
        CollectionAssert.AreEquivalent(
            new[] { "analyse.inventory", "analyse.dependencies" },
            prepareTask.DependsOn.ToList());
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_InventoryKind_MultiOrgProjects_BuildsScopedCaptureTasks()
    {
        // Arrange
        var identities = CreateModule("Identities", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var nodes = CreateModule("Nodes", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var builder = CreateBuilder(
            new[] { identities, nodes },
            new[] { CreateAnalyser("Inventory", Array.Empty<ModuleDependency>()) });

        var config = BuildOrganisationsConfig(
            ("Simulated", "", new[] { "ProjectA", "ProjectB" }),
            ("Simulated", "", new[] { "ProjectC" }));

        // Act
        var plan = await builder.BuildPlanAsync(
            config,
            JobKind.Inventory,
            PackageTestFactory.CreateLooseMock().Object,
            CancellationToken.None);

        // Assert
        var captureIds = plan.Tasks
            .Where(t => t.TaskKind == TaskKind.Capture)
            .Select(t => t.Id)
            .ToList();

        static bool ContainsId(IEnumerable<string> ids, string expected)
            => ids.Any(id => string.Equals(id, expected, StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(6, captureIds.Count, "Expected two capture modules across three project scopes");
        Assert.IsTrue(ContainsId(captureIds, "capture.identities.simulated.projecta"));
        Assert.IsTrue(ContainsId(captureIds, "capture.nodes.simulated.projecta"));
        Assert.IsTrue(ContainsId(captureIds, "capture.identities.simulated.projectb"));
        Assert.IsTrue(ContainsId(captureIds, "capture.nodes.simulated.projectb"));
        Assert.IsTrue(ContainsId(captureIds, "capture.identities.simulated.projectc"));
        Assert.IsTrue(ContainsId(captureIds, "capture.nodes.simulated.projectc"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_InventoryKind_NoProjectsConfigured_ProducesNoCaptureTasks()
    {
        // Arrange
        var identities = CreateModule("Identities", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var builder = CreateBuilder(new[] { identities }, new[] { CreateAnalyser("Inventory", Array.Empty<ModuleDependency>()) });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Organisations:0:Type"] = "Simulated",
                ["MigrationPlatform:Organisations:0:Enabled"] = "true"
            })
            .Build();

        // Act
        var plan = await builder.BuildPlanAsync(
            config,
            JobKind.Inventory,
            PackageTestFactory.CreateLooseMock().Object,
            CancellationToken.None);

        // Assert
        Assert.IsFalse(plan.Tasks.Any(t => t.TaskKind == TaskKind.Capture),
            "No capture tasks should be generated when no projects are configured");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_DependenciesKind_DependencyCaptureTasksDependOnAnalyseInventory()
    {
        // Arrange
        var identities = CreateModule("Identities", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var builder = CreateBuilder(
            new[] { identities },
            new[]
            {
                CreateAnalyser("Inventory", Array.Empty<ModuleDependency>()),
                CreateAnalyser("Dependencies", Array.Empty<ModuleDependency>())
            });

        var config = BuildOrganisationsConfig(("Simulated", "", new[] { "ProjectA", "ProjectB" }));

        // Act
        var plan = await builder.BuildPlanAsync(
            config,
            JobKind.Dependencies,
            PackageTestFactory.CreateLooseMock().Object,
            CancellationToken.None);

        // Assert
        var dependencyCaptureTasks = plan.Tasks.Where(t => t.Id.StartsWith("capture.dependencies.", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.AreEqual(2, dependencyCaptureTasks.Count);
        foreach (var task in dependencyCaptureTasks)
        {
            Assert.IsNotNull(task.DependsOn);
            CollectionAssert.AreEqual(new[] { "analyse.inventory" }, task.DependsOn.ToList());
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task BuildPlanAsync_DependenciesKind_AnalyseDependenciesDependsOnAllDependencyCaptures()
    {
        // Arrange
        var identities = CreateModule("Identities", Array.Empty<ModuleDependency>(), supportsInventory: true);
        var builder = CreateBuilder(
            new[] { identities },
            new[]
            {
                CreateAnalyser("Inventory", Array.Empty<ModuleDependency>()),
                CreateAnalyser("Dependencies", Array.Empty<ModuleDependency>())
            });

        var config = BuildOrganisationsConfig(("Simulated", "", new[] { "ProjectA", "ProjectB", "ProjectC" }));

        // Act
        var plan = await builder.BuildPlanAsync(
            config,
            JobKind.Dependencies,
            PackageTestFactory.CreateLooseMock().Object,
            CancellationToken.None);

        // Assert
        var expectedDependencyCaptureIds = plan.Tasks
            .Where(t => t.Id.StartsWith("capture.dependencies.", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var analyseDependenciesTask = plan.Tasks.Single(t => t.Id == "analyse.dependencies");
        Assert.IsNotNull(analyseDependenciesTask.DependsOn);
        var actualDependsOn = analyseDependenciesTask.DependsOn
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        CollectionAssert.AreEqual(expectedDependencyCaptureIds, actualDependsOn);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JobExecutionPlanBuilder CreateBuilder(
        IEnumerable<IModule> modules,
        IEnumerable<IAnalyser>? analysers = null,
        ILogger<JobExecutionPlanBuilder>? logger = null)
    {
        var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
        var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
        phaseService
            .Setup(s => s.ReadPhaseRecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPhaseRecord());
        phaseFactory
            .Setup(f => f.Create(It.IsAny<IPackageAccess>()))
            .Returns(phaseService.Object);

        return new JobExecutionPlanBuilder(
            modules,
            analysers ?? [],
            phaseFactory.Object,
            logger ?? NullLogger<JobExecutionPlanBuilder>.Instance,
            package: PackageTestFactory.CreateLooseMock().Object);
    }

    private static IModule CreateModule(
        string name,
        ModuleDependency[] dependsOn,
        bool supportsInventory = false,
        bool supportsPrepare = false)
    {
        var module = new Mock<IModule>(MockBehavior.Loose);
        module.SetupGet(m => m.Name).Returns(name);
        module.SetupGet(m => m.DependsOn).Returns((IReadOnlyList<ModuleDependency>)dependsOn);
        module.SetupGet(m => m.SupportsExport).Returns(true);
        module.SetupGet(m => m.SupportsImport).Returns(true);
        module.SetupGet(m => m.SupportsInventory).Returns(supportsInventory);
        module.SetupGet(m => m.SupportsPrepare).Returns(supportsPrepare);
        module.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());
        module.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());
        module.Setup(m => m.PrepareAsync(It.IsAny<PrepareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());
        module.Setup(m => m.ValidateAsync(It.IsAny<ValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());
        return module.Object;
    }

    private static IConfiguration BuildOrganisationsConfig(params (string Type, string Url, string[] Projects)[] organisations)
    {
        var values = new Dictionary<string, string?>();
        for (int i = 0; i < organisations.Length; i++)
        {
            var org = organisations[i];
            values[$"MigrationPlatform:Organisations:{i}:Type"] = org.Type;
            if (!string.IsNullOrWhiteSpace(org.Url))
                values[$"MigrationPlatform:Organisations:{i}:Url"] = org.Url;
            values[$"MigrationPlatform:Organisations:{i}:Enabled"] = "true";

            for (int p = 0; p < org.Projects.Length; p++)
                values[$"MigrationPlatform:Organisations:{i}:Projects:{p}"] = org.Projects[p];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IAnalyser CreateAnalyser(string name, ModuleDependency[] dependsOn)
    {
        var analyser = new Mock<IAnalyser>(MockBehavior.Loose);
        analyser.SetupGet(a => a.Name).Returns(name);
        analyser.SetupGet(a => a.DependsOn).Returns((IReadOnlyList<ModuleDependency>)dependsOn);
        analyser.Setup(a => a.AnalyseAsync(It.IsAny<AnalyseContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());
        return analyser.Object;
    }

    // Fake module/analyser types for testing. ModuleDependency constrains targets to
    // IModule (module phases) / IAnalyser (Analyse phase) — see ADR-0027 (MC-L2) —
    // so the fakes implement the respective contracts. Names are always overridden
    // via ModuleNameOverride, so the members are never invoked.
    private abstract class FakeModuleBase : IModule
    {
        public string Name => GetType().Name;

        public IModuleContract Contract => new ModuleContract(Name, [], [], []);
        public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
        public bool SupportsExport => false;
        public bool SupportsInventory => false;
        public bool SupportsPrepare => false;
        public bool SupportsImport => false;
        public bool SupportsValidate => false;
        public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct) => throw new NotSupportedException();
    }

    private abstract class FakeAnalyserBase : IAnalyser
    {
        public string Name => GetType().Name;

        public IModuleContract Contract => new ModuleContract(Name, [], [], []);
        public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeIdentitiesModule : FakeModuleBase { }
    private sealed class FakeNodesModule : FakeModuleBase { }
    private sealed class FakeTeamsModule : FakeModuleBase { }
    private sealed class FakeWorkItemsModule : FakeModuleBase { }
    private sealed class FakeModuleA : FakeModuleBase { }
    private sealed class FakeModuleB : FakeModuleBase { }
    private sealed class FakeNonExistentModule : FakeModuleBase { }
    private sealed class FakeInventoryAnalyser : FakeAnalyserBase { }
    private sealed class FakeDependenciesAnalyser : FakeAnalyserBase { }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static IConfiguration AllEnabledConfig()
    {
        // Default: all modules enabled
        return new ConfigurationBuilder().Build();
    }
}
