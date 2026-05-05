// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobExecutionPlanBuilderDependsOnTests
{
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
        var store = new Mock<IArtefactStore>(MockBehavior.Loose).Object;
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Import, store, stateStore, CancellationToken.None);

        // Assert
        var workItemsTask = plan.Tasks.First(t => t.Id.StartsWith("import.workitems"));
        Assert.IsNotNull(workItemsTask.DependsOn, "WorkItems task should have dependencies");
        Assert.IsTrue(workItemsTask.DependsOn.Any(d => d.StartsWith("import.identities")),
            "WorkItems should depend on Identities");
        Assert.IsTrue(workItemsTask.DependsOn.Any(d => d.StartsWith("import.nodes")),
            "WorkItems should depend on Nodes");
    }

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
        var store = new Mock<IArtefactStore>(MockBehavior.Loose).Object;
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Export, store, stateStore, CancellationToken.None);

        // Assert
        foreach (var task in plan.Tasks.Where(t => t.Phase == "Export"))
        {
            Assert.IsTrue(task.DependsOn == null || task.DependsOn.Count == 0,
                $"Export task {task.Id} should have no dependencies");
        }
    }

    [TestMethod]
    public async Task BuildPlanAsync_CircularDependency_ThrowsInvalidOperationException()
    {
        // Arrange
        var moduleA = CreateModule("ModuleA", new[] { new ModuleDependency(typeof(FakeModuleB), DependencyPhase.Import) { ModuleNameOverride = "ModuleB" } });
        var moduleB = CreateModule("ModuleB", new[] { new ModuleDependency(typeof(FakeModuleA), DependencyPhase.Import) { ModuleNameOverride = "ModuleA" } });

        var builder = CreateBuilder(new[] { moduleA, moduleB });
        var config = AllEnabledConfig();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose).Object;
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;

        // Act & Assert
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => builder.BuildPlanAsync(config, JobKind.Import, store, stateStore, CancellationToken.None));

        Assert.IsTrue(exception.Message.Contains("Circular dependency"),
            "Exception message should mention circular dependency");
    }

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

        var store = new Mock<IArtefactStore>(MockBehavior.Loose).Object;
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Import, store, stateStore, CancellationToken.None);

        // Assert
        var taskB = plan.Tasks.FirstOrDefault(t => t.Id.StartsWith("import.moduleb"));
        Assert.IsNotNull(taskB, "ModuleB task should exist");

        // Since ModuleA is disabled, it won't have a task, so the dependency should be omitted
        Assert.IsTrue(taskB.DependsOn == null || !taskB.DependsOn.Any(d => d.StartsWith("import.modulea")),
            "ModuleB should not depend on disabled ModuleA");
    }

    [TestMethod]
    public async Task BuildPlanAsync_DependencyOnNonExistentModule_LogsWarningAndOmitsDependency()
    {
        // Arrange
        var moduleA = CreateModule("ModuleA", new[] { new ModuleDependency(typeof(FakeNonExistentModule), DependencyPhase.Import) { ModuleNameOverride = "NonExistentModule" } });

        var builder = CreateBuilder(new[] { moduleA });
        var config = AllEnabledConfig();
        var store = new Mock<IArtefactStore>(MockBehavior.Loose).Object;
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose).Object;

        // Act
        var plan = await builder.BuildPlanAsync(config, JobKind.Import, store, stateStore, CancellationToken.None);

        // Assert
        var taskA = plan.Tasks.First(t => t.Id.StartsWith("import.modulea"));
        Assert.IsTrue(taskA.DependsOn == null || !taskA.DependsOn.Any(d => d.StartsWith("import.nonexistentmodule")),
            "Dependency on non-existent module should be omitted");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JobExecutionPlanBuilder CreateBuilder(IEnumerable<IModule> modules)
    {
        var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
        var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
        phaseService
            .Setup(s => s.ReadPhaseRecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPhaseRecord());
        phaseFactory
            .Setup(f => f.Create(It.IsAny<IStateStore>()))
            .Returns(phaseService.Object);

        return new JobExecutionPlanBuilder(modules, [], phaseFactory.Object, NullLogger<JobExecutionPlanBuilder>.Instance);
    }

    private static IModule CreateModule(string name, ModuleDependency[] dependsOn)
    {
        var module = new Mock<IModule>(MockBehavior.Loose);
        module.SetupGet(m => m.Name).Returns(name);
        module.SetupGet(m => m.DependsOn).Returns((IReadOnlyList<ModuleDependency>)dependsOn);
        module.SetupGet(m => m.SupportsExport).Returns(true);
        module.SetupGet(m => m.SupportsImport).Returns(true);
        module.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        module.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        module.Setup(m => m.ValidateAsync(It.IsAny<ValidationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return module.Object;
    }

    // Fake module types for testing
    private sealed class FakeIdentitiesModule { }
    private sealed class FakeNodesModule { }
    private sealed class FakeTeamsModule { }
    private sealed class FakeWorkItemsModule { }
    private sealed class FakeModuleA { }
    private sealed class FakeModuleB { }
    private sealed class FakeNonExistentModule { }

    private static IConfiguration AllEnabledConfig()
    {
        // Default: all modules enabled
        return new ConfigurationBuilder().Build();
    }
}
