// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Prepare;

[TestClass]
public sealed class PrepareFeaturesDslTests
{
    [TestMethod]
    public async Task Prepare_AllModulesEnabled_WritesReportPerModule_ForSimulated()
    {
        var result = await PrepareReportsScenario
            .Create("Simulated")
            .RunAsync();

        result.ShouldContainReport("WorkItems/prepare-report.json");
        result.ShouldContainReport("Identities/prepare-report.json");
        result.ShouldContainReport("Nodes/prepare-report.json");
        result.ShouldContainReport("Teams/prepare-report.json");
    }

    [TestMethod]
    public async Task Prepare_AllModulesEnabled_WritesReportPerModule_ForAzureDevOpsServices()
    {
        var result = await PrepareReportsScenario
            .Create("AzureDevOpsServices")
            .RunAsync();

        result.ShouldContainReport("WorkItems/prepare-report.json");
        result.ShouldContainReport("Identities/prepare-report.json");
        result.ShouldContainReport("Nodes/prepare-report.json");
        result.ShouldContainReport("Teams/prepare-report.json");
    }

    [TestMethod]
    public async Task Prepare_UnresolvedIdentities_CompletesWithWarningTelemetry_ForSimulated()
    {
        var result = await PrepareWarningScenario
            .Create("Simulated")
            .RunAsync();

        result.ShouldComplete();
        result.ShouldReportWarningReadiness();
    }

    [TestMethod]
    public async Task Prepare_UnresolvedIdentities_CompletesWithWarningTelemetry_ForAzureDevOpsServices()
    {
        var result = await PrepareWarningScenario
            .Create("AzureDevOpsServices")
            .RunAsync();

        result.ShouldComplete();
        result.ShouldReportWarningReadiness();
    }

    [TestMethod]
    public async Task Prepare_InMigratePipeline_RunsBeforeImport_ForSimulated()
    {
        var result = await MigratePipelineOrderingScenario
            .Create("Simulated")
            .RunAsync();

        result.ShouldOrderExportBeforeImport();
    }

    [TestMethod]
    public async Task Prepare_InMigratePipeline_RunsBeforeImport_ForAzureDevOpsServices()
    {
        var result = await MigratePipelineOrderingScenario
            .Create("AzureDevOpsServices")
            .RunAsync();

        result.ShouldOrderExportBeforeImport();
    }

    [TestMethod]
    public async Task Prepare_ModuleWithAnalyserDependsOn_HoistsAnalyseBeforePrepare()
    {
        var result = await PrepareAnalyseDependencyScenario.Create().RunAsync();

        result.ShouldContainTask("analyse.inventory");
        result.ShouldContainTask("prepare.modulea");
        result.ShouldDeclareDependency("prepare.modulea", "analyse.inventory");
    }

    [TestMethod]
    public async Task Prepare_TfsSourceOnlyModule_SkipsGracefullyWithWarning()
    {
        var logger = new CapturingLogger();
        var module = new SourceOnlyModule(logger);
        var context = new PrepareContext
        {
            Job = new Job { JobId = "prepare-tfs-1", Kind = JobKind.Prepare },
            Package = Mock.Of<IPackageAccess>(),
            TargetEndpoint = CreateTargetEndpoint("TeamFoundationServer"),
            ProgressSink = Mock.Of<IProgressSink>()
        };

        var result = await module.PrepareAsync(context, CancellationToken.None);

        Assert.AreEqual(JobTaskStatus.Skipped, result.Status);
        StringAssert.Contains(result.StatusMessage ?? string.Empty, "Prepare phase is not supported");
        Assert.IsTrue(logger.WarningMessages.Any(m => m.Contains("Prepare phase is not supported by module TfsWorkItemsModule", StringComparison.Ordinal)));
    }

    private sealed class PrepareReportsScenario
    {
        private readonly string _connectorType;

        private PrepareReportsScenario(string connectorType) => _connectorType = connectorType;

        public static PrepareReportsScenario Create(string connectorType) => new(connectorType);

        public async Task<PrepareReportsResult> RunAsync()
        {
            var persisted = new List<string>();
            var package = CreatePackageCaptureMock(persisted, out _);

            await CreateWorkItemsModule(_connectorType).PrepareAsync(CreatePrepareContext(package.Object, _connectorType), CancellationToken.None);
            await CreateIdentitiesModule(_connectorType).PrepareAsync(CreatePrepareContext(package.Object, _connectorType), CancellationToken.None);
            await CreateNodesModule(_connectorType).PrepareAsync(CreatePrepareContext(package.Object, _connectorType), CancellationToken.None);
            await CreateTeamsModule(_connectorType).PrepareAsync(CreatePrepareContext(package.Object, _connectorType), CancellationToken.None);

            return new PrepareReportsResult(persisted);
        }
    }

    private sealed class PrepareWarningScenario
    {
        private readonly string _connectorType;

        private PrepareWarningScenario(string connectorType) => _connectorType = connectorType;

        public static PrepareWarningScenario Create(string connectorType) => new(connectorType);

        public async Task<PrepareWarningResult> RunAsync()
        {
            var persistedPaths = new List<string>();
            var persistedContent = new Dictionary<string, string>(StringComparer.Ordinal);
            var package = CreatePackageCaptureMock(persistedPaths, out var contentCapture);

            var module = CreateWorkItemsModule(
                _connectorType,
                [
                    new StaticImportFailurePattern(
                        [
                            new ImportFailureFinding(
                                "WORKITEMS_PREPARE_UNRESOLVED_IDENTITY",
                                ImportFailureSeverity.Warning,
                                "fallback@source.example",
                                "No explicit identity mapping was found.",
                                "Add an explicit mapping.")
                        ])
                ]);

            var context = CreatePrepareContext(package.Object, _connectorType);
            var execution = await module.PrepareAsync(context, CancellationToken.None);

            foreach (var (path, content) in contentCapture)
            {
                persistedContent[path] = content;
            }

            return new PrepareWarningResult(execution, persistedPaths, persistedContent);
        }
    }

    private sealed class PrepareAnalyseDependencyScenario
    {
        public static PrepareAnalyseDependencyScenario Create() => new();

        public async Task<PrepareAnalyseDependencyResult> RunAsync()
        {
            var module = new Mock<IModule>(MockBehavior.Loose);
            module.SetupGet(m => m.Name).Returns("ModuleA");
            module.SetupGet(m => m.SupportsPrepare).Returns(true);
            module.SetupGet(m => m.SupportsExport).Returns(true);
            module.SetupGet(m => m.SupportsImport).Returns(true);
            module.SetupGet(m => m.DependsOn).Returns(
                new[]
                {
                    new ModuleDependency(typeof(FakeInventoryAnalyser), DependencyPhase.Analyse) { ModuleNameOverride = "Inventory" }
                });

            var analyser = new Mock<IAnalyser>(MockBehavior.Loose);
            analyser.SetupGet(a => a.Name).Returns("Inventory");
            analyser.SetupGet(a => a.DependsOn).Returns(System.Array.Empty<ModuleDependency>());
            analyser.Setup(a => a.AnalyseAsync(It.IsAny<AnalyseContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaskExecutionResult.Completed());

            var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
            var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
            phaseService.Setup(s => s.ReadPhaseRecordAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new JobPhaseRecord());
            phaseFactory.Setup(f => f.Create(It.IsAny<IPackageAccess>())).Returns(phaseService.Object);

            var builder = new JobExecutionPlanBuilder(
                [module.Object],
                [analyser.Object],
                phaseFactory.Object,
                NullLogger<JobExecutionPlanBuilder>.Instance,
                package: Mock.Of<IPackageAccess>());

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["MigrationPlatform:Modules:ModuleA:Enabled"] = "true"
                    })
                .Build();

            var plan = await builder.BuildPlanAsync(config, JobKind.Prepare, Mock.Of<IPackageAccess>(), CancellationToken.None);
            return new PrepareAnalyseDependencyResult(plan.Tasks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class MigratePipelineOrderingScenario
    {
        private readonly string _connectorType;

        private MigratePipelineOrderingScenario(string connectorType) => _connectorType = connectorType;

        public static MigratePipelineOrderingScenario Create(string connectorType) => new(connectorType);

        public async Task<MigratePipelineOrderingResult> RunAsync()
        {
            var modules = CreateStandardModules();

            var phaseFactory = new Mock<IPhaseTrackingServiceFactory>(MockBehavior.Loose);
            var phaseService = new Mock<IPhaseTrackingService>(MockBehavior.Loose);
            phaseService.Setup(s => s.ReadPhaseRecordAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new JobPhaseRecord());
            phaseFactory.Setup(f => f.Create(It.IsAny<IPackageAccess>())).Returns(phaseService.Object);

            var builder = new JobExecutionPlanBuilder(
                modules,
                [],
                phaseFactory.Object,
                NullLogger<JobExecutionPlanBuilder>.Instance,
                package: Mock.Of<IPackageAccess>());

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["MigrationPlatform:Source:Type"] = _connectorType,
                        ["MigrationPlatform:Source:Project"] = "SourceProject",
                        ["MigrationPlatform:Target:Type"] = _connectorType,
                        ["MigrationPlatform:Target:Project"] = "TargetProject",
                        ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
                        ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
                        ["MigrationPlatform:Modules:Teams:Enabled"] = "true",
                        ["MigrationPlatform:Modules:WorkItems:Enabled"] = "true"
                    })
                .Build();

            var plan = await builder.BuildPlanAsync(config, JobKind.Migrate, Mock.Of<IPackageAccess>(), CancellationToken.None);
            return new MigratePipelineOrderingResult(plan);
        }

        private static IReadOnlyList<IModule> CreateStandardModules()
        {
            return
            [
                CreateModule("Identities"),
                CreateModule("Nodes"),
                CreateModule("Teams"),
                CreateModule("WorkItems")
            ];
        }

        private static IModule CreateModule(string name)
        {
            var module = new Mock<IModule>(MockBehavior.Loose);
            module.SetupGet(m => m.Name).Returns(name);
            module.SetupGet(m => m.DependsOn).Returns(System.Array.Empty<ModuleDependency>());
            module.SetupGet(m => m.SupportsExport).Returns(true);
            module.SetupGet(m => m.SupportsImport).Returns(true);
            module.SetupGet(m => m.SupportsPrepare).Returns(true);
            module.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaskExecutionResult.Completed());
            module.Setup(m => m.PrepareAsync(It.IsAny<PrepareContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaskExecutionResult.Completed());
            module.Setup(m => m.ImportAsync(It.IsAny<ImportContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaskExecutionResult.Completed());
            return module.Object;
        }
    }

    private sealed class FakeInventoryAnalyser : IAnalyser
    {
        public string Name => "Inventory";
        public IReadOnlyList<ModuleDependency> DependsOn => System.Array.Empty<ModuleDependency>();
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken cancellationToken) => Task.FromResult(TaskExecutionResult.Completed());
    }

    private sealed class SourceOnlyModule(ILogger logger) : ModuleBase(logger)
    {
        public override string Name => "TfsWorkItemsModule";
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> WarningMessages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningMessages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }

    private sealed class PrepareReportsResult(IReadOnlyCollection<string> paths)
    {
        public void ShouldContainReport(string path)
        {
            Assert.IsTrue(
                paths.Any(candidate => candidate.EndsWith(path, StringComparison.OrdinalIgnoreCase)),
                $"Expected persisted report at '{path}'.");
        }
    }

    private sealed class PrepareWarningResult(
        TaskExecutionResult execution,
        IReadOnlyCollection<string> persistedPaths,
        IReadOnlyDictionary<string, string> persistedContent)
    {
        public void ShouldComplete() => Assert.AreEqual(JobTaskStatus.Completed, execution.Status);

        public void ShouldReportWarningReadiness()
        {
            var reportPath = persistedPaths.FirstOrDefault(path => path.EndsWith("WorkItems/prepare-report.json", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reportPath));
            var report = JsonSerializer.Deserialize<PrepareReport>(persistedContent[reportPath!]);
            Assert.IsNotNull(report);
            Assert.AreEqual(WorkItemsPrepareReadinessResult.Ready, report.Readiness);
            Assert.AreEqual(1, report.UnresolvedCount);
            Assert.AreEqual(PrepareIssueSeverity.Warning, report.UnresolvedItems[0].Severity);
        }
    }

    private sealed class PrepareAnalyseDependencyResult(IReadOnlyDictionary<string, JobTask> tasks)
    {
        public void ShouldContainTask(string taskId) => Assert.IsTrue(tasks.ContainsKey(taskId), $"Expected task '{taskId}'.");

        public void ShouldDeclareDependency(string taskId, string dependsOn)
        {
            Assert.IsTrue(tasks.ContainsKey(taskId), $"Expected task '{taskId}'.");
            var task = tasks[taskId];
            Assert.IsNotNull(task.DependsOn);
            Assert.IsTrue(task.DependsOn.Contains(dependsOn), $"Expected '{taskId}' to depend on '{dependsOn}'.");
        }
    }

    private sealed class MigratePipelineOrderingResult(JobTaskList plan)
    {
        public void ShouldOrderExportBeforeImport()
        {
            var exportTasks = plan.Tasks.Where(t => string.Equals(t.Phase, "Export", StringComparison.OrdinalIgnoreCase)).ToList();
            var importTasks = plan.Tasks.Where(t => string.Equals(t.Phase, "Import", StringComparison.OrdinalIgnoreCase)).ToList();

            Assert.IsTrue(exportTasks.Count > 0, "Expected export tasks in migrate plan.");
            Assert.IsTrue(importTasks.Count > 0, "Expected import tasks in migrate plan.");
            Assert.IsTrue(exportTasks.Max(t => t.Order) < importTasks.Min(t => t.Order),
                "Expected export tasks to complete before import tasks.");
        }
    }

    private sealed class StaticImportFailurePattern(IReadOnlyList<ImportFailureFinding> findings) : IImportFailurePattern
    {
        public string PatternCode => "TEST";

        public Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
            ImportFailurePatternContext context,
            CancellationToken cancellationToken) => Task.FromResult(findings);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static Mock<IPackageAccess> CreatePackageCaptureMock(
        ICollection<string> persistedPaths,
        out Dictionary<string, string> capturedContent)
    {
        var content = new Dictionary<string, string>(StringComparer.Ordinal);
        capturedContent = content;
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                if (context.IsCollectionRequest &&
                    string.Equals(context.Module, "WorkItems", StringComparison.OrdinalIgnoreCase))
                {
                    return EnumeratePathsAsync(["WorkItems/2026-05-13/638827200000000000-42-0/revision.json"]);
                }

                return EnumeratePathsAsync([]);
            });

        package
            .Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((ctx, payload, _) =>
            {
                var path = ComposePersistedPath(ctx);
                persistedPaths.Add(path);
                payload.Content.Position = 0;
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, false, 1024, leaveOpen: true);
                content[path] = reader.ReadToEnd();
                payload.Content.Position = 0;
            })
            .Returns(ValueTask.CompletedTask);

        package
            .Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return package;
    }

    private static string ComposePersistedPath(PackageContentContext context)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Organisation))
            segments.Add(context.Organisation!);
        if (!string.IsNullOrWhiteSpace(context.Project))
            segments.Add(context.Project!);
        if (!string.IsNullOrWhiteSpace(context.Module))
            segments.Add(context.Module!);
        if (!string.IsNullOrWhiteSpace(context.Address?.RelativePath))
            segments.Add(context.Address.RelativePath);

        return string.Join("/", segments);
    }

    private static async IAsyncEnumerable<string> EnumeratePathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            yield return path;
        }

        await Task.CompletedTask;
    }

    private static PrepareContext CreatePrepareContext(IPackageAccess package, string connectorType)
    {
        return new PrepareContext
        {
            Job = new Job { JobId = $"prepare-{connectorType.ToLowerInvariant()}", Kind = JobKind.Prepare },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>(),
            TargetEndpoint = CreateTargetEndpoint(connectorType)
        };
    }

    private static ISourceEndpointInfo CreateSourceEndpoint(string connectorType)
    {
        var endpoint = new Mock<ISourceEndpointInfo>(MockBehavior.Loose);
        endpoint.SetupGet(e => e.Project).Returns("ProjectA");
        endpoint.SetupGet(e => e.Url).Returns("https://source.example");
        endpoint.SetupGet(e => e.OrganisationSlug).Returns("source");
        endpoint.SetupGet(e => e.ConnectorType).Returns(connectorType);
        return endpoint.Object;
    }

    private static ITargetEndpointInfo CreateTargetEndpoint(string connectorType)
    {
        var endpoint = new Mock<ITargetEndpointInfo>(MockBehavior.Loose);
        endpoint.SetupGet(e => e.Project).Returns("ProjectA");
        endpoint.SetupGet(e => e.Url).Returns("https://target.example");
        endpoint.SetupGet(e => e.ConnectorType).Returns(connectorType);
        return endpoint.Object;
    }

    private static WorkItemsModule CreateWorkItemsModule(string connectorType, IEnumerable<IImportFailurePattern>? patterns = null)
    {
        return new WorkItemsModule(
            Mock.Of<Abstractions.Agent.Export.IWorkItemRevisionSourceFactory>(),
            NullLogger<WorkItemsModule>.Instance,
            Options.Create(new WorkItemsModuleOptions()),
            CreateSourceEndpoint(connectorType),
            NullLogger<WorkItemsImportRuntime>.Instance,
            Mock.Of<Abstractions.Agent.WorkItems.IWorkItemTargetFactory>(),
            Mock.Of<Abstractions.Agent.WorkItems.IWorkItemResolutionStrategyFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IWorkItemResolutionProcessorFactory>(),
            CreateTargetEndpoint(connectorType),
            identityMappingService: Mock.Of<Abstractions.Agent.Identity.IIdentityMappingService>(),
            nodeTranslationTool: Mock.Of<Abstractions.Agent.Tools.INodeTranslationTool>(),
            fieldTransformTool: Mock.Of<Abstractions.Agent.Tools.IFieldTransformTool>(),
            importFailurePatterns: patterns);
    }

    private static IdentitiesModule CreateIdentitiesModule(string connectorType)
    {
        return new IdentitiesModule(
            NullLogger<IdentitiesModule>.Instance,
            Options.Create(new IdentitiesModuleOptions()),
            CreateSourceEndpoint(connectorType),
            Mock.Of<IIdentitiesOrchestrator>(),
            PlatformMetrics: null,
            identitySource: null,
            checkpointingFactory: null,
            identityTranslationTool: Mock.Of<Abstractions.Agent.Tools.IIdentityTranslationTool>());
    }

    private static NodesModule CreateNodesModule(string connectorType)
    {
        return new NodesModule(
            NullLogger<NodesModule>.Instance,
            Options.Create(new NodesModuleOptions()),
            CreateSourceEndpoint(connectorType),
            Mock.Of<INodesOrchestrator>(),
            PlatformMetrics: null,
            capture: null,
            targetEndpointInfo: CreateTargetEndpoint(connectorType),
            reader: null,
            checkpointingFactory: null);
    }

    private static TeamsModule CreateTeamsModule(string connectorType)
    {
        return new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions()),
            CreateSourceEndpoint(connectorType),
            CreateTargetEndpoint(connectorType),
            Mock.Of<ITeamsOrchestrator>(),
            PlatformMetrics: null,
            teamSource: null,
            teamTarget: null,
            checkpointingFactory: null);
    }

}
