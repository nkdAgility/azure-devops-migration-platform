// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

internal static class JobAgentWorkerTestHelper
{
    private static readonly System.Reflection.MethodInfo OnJobMethod =
        typeof(JobAgentWorker).GetMethod(
            "OnJobAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    public static Task InvokeJobAsync(
        JobAgentWorker worker,
        Job job,
        HttpClient client,
        string leaseId,
        CancellationToken cancellationToken)
    {
        return (Task)OnJobMethod.Invoke(worker, new object[] { job, client, leaseId, cancellationToken })!;
    }
}

[TestClass]
public sealed class JobAgentWorkerDispatchTests
{
    private Mock<IPackagePreparer> _packagePreparer = null!;
    private Mock<IPackageAccess> _package = null!;
    private Mock<IProgressSink> _progressSink = null!;
    private Mock<ICheckpointingServiceFactory> _checkpointingFactory = null!;
    private Mock<IPhaseTrackingServiceFactory> _phaseTrackingFactory = null!;
    private Mock<ICheckpointingService> _checkpointer = null!;
    private Mock<IPhaseTrackingService> _phaseTracker = null!;
    private Mock<IPackageMigrationConfigLoader> _packageMigrationConfigLoader = null!;
    private Mock<IJobExecutionPlanBuilder> _planBuilder = null!;
    private Mock<IJobPlanExecutor> _planExecutor = null!;
    private Mock<ICurrentPackageConfigAccessor> _currentPackageConfigAccessor = null!;
    private Mock<ICurrentAgentJobContextAccessor> _currentJobContextAccessor = null!;
    private Mock<ICurrentJobEndpointAccessor> _currentJobEndpointAccessor = null!;
    private UnifiedWorkerEventWriter _eventWriter = null!;
    private Mock<IJobMetricsStore> _metricsStore = null!;
    private Mock<IJobSnapshotStore> _snapshotStore = null!;
    private Mock<IActiveJobState> _activeJobState = null!;
    private MockHttpMessageHandler _httpHandler = null!;
    private ActiveLeaseState _leaseState = null!;
    private ActivePackageState _packageState = null!;
    private IConfiguration _packageConfiguration = null!;
    private JobTaskList _plan = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private ILogger<JobAgentWorker> _logger = null!;
    private IFlushable[] _flushables = null!;

    [TestInitialize]
    public void Setup()
    {
        _packagePreparer = new Mock<IPackagePreparer>();
        _package = new Mock<IPackageAccess>();
        _package
            .Setup(p => p.RequestMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageMetaResult("memory://migration-config.json", null));
        _progressSink = new Mock<IProgressSink>();
        _checkpointingFactory = new Mock<ICheckpointingServiceFactory>();
        _phaseTrackingFactory = new Mock<IPhaseTrackingServiceFactory>();
        _checkpointer = new Mock<ICheckpointingService>();
        _phaseTracker = new Mock<IPhaseTrackingService>();
        _packageMigrationConfigLoader = new Mock<IPackageMigrationConfigLoader>();
        _planBuilder = new Mock<IJobExecutionPlanBuilder>();
        _planExecutor = new Mock<IJobPlanExecutor>();
        _currentPackageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>();
        _currentJobContextAccessor = new Mock<ICurrentAgentJobContextAccessor>();
        _currentJobEndpointAccessor = new Mock<ICurrentJobEndpointAccessor>();
        _metricsStore = new Mock<IJobMetricsStore>();
        _snapshotStore = new Mock<IJobSnapshotStore>();
        _activeJobState = new Mock<IActiveJobState>();
        _httpHandler = new MockHttpMessageHandler();
        _leaseState = new ActiveLeaseState();
        _packageState = new ActivePackageState();
        var httpFactoryForWriter = new Mock<IHttpClientFactory>();
        httpFactoryForWriter.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(_httpHandler) { BaseAddress = new Uri("http://localhost:5100") });
        _eventWriter = new UnifiedWorkerEventWriter(
            httpFactoryForWriter.Object,
            _leaseState,
            NullLogger<UnifiedWorkerEventWriter>.Instance);
        _logger = NullLogger<JobAgentWorker>.Instance;

        _packageConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Url"] = "https://simulated.example/source",
                ["MigrationPlatform:Source:Project"] = "SourceProject",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Target:Url"] = "https://simulated.example/target",
                ["MigrationPlatform:Target:Project"] = "TargetProject",
                ["MigrationPlatform:Mode"] = "Export",
            })
            .Build();

        _plan = new JobTaskList
        {
            Tasks = new List<JobTask>
            {
                new()
                {
                    Id = "export.workitems",
                    Name = "WorkItems Export",
                    TaskKind = TaskKind.Export,
                    Phase = "export",
                    ProjectName = "SourceProject",
                    Order = 0,
                    Status = JobTaskStatus.Pending,
                    DependsOn = Array.Empty<string>()
                }
            }.AsReadOnly()
        };

        _packagePreparer
            .Setup(preparer => preparer.PrepareForImportAsync(It.IsAny<IConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _checkpointingFactory
            .Setup(factory => factory.Create(It.IsAny<IPackageAccess>()))
            .Returns(_checkpointer.Object);

        _checkpointer
            .Setup(checkpointer => checkpointer.DeleteCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _checkpointer
            .Setup(checkpointer => checkpointer.DeleteContinuationTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _phaseTrackingFactory
            .Setup(factory => factory.Create(It.IsAny<IPackageAccess>()))
            .Returns(_phaseTracker.Object);

        _phaseTracker
            .Setup(tracker => tracker.ReadPhaseRecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPhaseRecord());

        _packageMigrationConfigLoader
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_packageConfiguration);

        _planBuilder
            .Setup(builder => builder.BuildAndSaveAsync(
                It.IsAny<IConfiguration>(),
                It.IsAny<JobKind>(),
                It.IsAny<IPackageAccess>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_plan);

        _planExecutor
            .Setup(executor => executor.ExportAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
                It.IsAny<InventoryContext?>(),
                It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
                It.IsAny<ExportContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _planExecutor
            .Setup(executor => executor.ImportAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<ImportContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _planExecutor
            .Setup(executor => executor.DispatchTasksAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
                It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
                It.IsAny<InventoryContext?>(),
                It.IsAny<ExportContext?>(),
                It.IsAny<ImportContext?>(),
                It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _flushables =
        [
            new PackageProgressSink(_packageState, NullLogger<PackageProgressSink>.Instance, _package.Object),
            new PackageLoggerProvider(_packageState, Options.Create(new DiagnosticLogOptions()), new ServiceCollection().AddSingleton(_package.Object).BuildServiceProvider()),
        ];

        var services = new ServiceCollection();
        services.AddSingleton<IModule>(new FakeModule("WorkItems", supportsPrepare: true));
        services.AddSingleton<ISourceEndpointInfo>(new FakeSourceEndpointInfo());
        services.AddSingleton<ITargetEndpointInfo>(new FakeTargetEndpointInfo());
        services.AddSingleton<IAnalyser>(new FakeAnalyser("Dependencies"));
        services.AddSingleton<IJobExecutionPlanBuilder>(_planBuilder.Object);
        services.AddSingleton<IJobPlanExecutor>(_planExecutor.Object);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Export_RoutesToExportPlanExecution()
    {
        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-export",
            CancellationToken.None);

        _planExecutor.Verify(executor => executor.ExportAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<ExportContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _planExecutor.Verify(executor => executor.DispatchTasksAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<ExportContext?>(),
            It.IsAny<ImportContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Export_PassesConfiguredAndResolvedSourceEndpointAliases()
    {
        var configuredSourceUrl = "configured://source-alias";
        _packageConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Url"] = configuredSourceUrl,
                ["MigrationPlatform:Source:Project"] = "SourceProject",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Target:Url"] = "https://simulated.example/target",
                ["MigrationPlatform:Target:Project"] = "TargetProject",
                ["MigrationPlatform:Mode"] = "Export",
            })
            .Build();

        _packageMigrationConfigLoader
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_packageConfiguration);

        IReadOnlyDictionary<string, OrganisationEndpoint>? capturedEndpoints = null;
        _planExecutor
            .Setup(executor => executor.ExportAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
                It.IsAny<InventoryContext?>(),
                It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
                It.IsAny<ExportContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<JobTaskList, IReadOnlyDictionary<string, IModule>, IReadOnlyDictionary<string, IAnalyser>, InventoryContext?, IReadOnlyDictionary<string, OrganisationEndpoint>?, ExportContext, CancellationToken>(
                (_, _, _, _, endpoints, _, _) => capturedEndpoints = endpoints)
            .ReturnsAsync(true);

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-export",
            CancellationToken.None);

        Assert.IsNotNull(capturedEndpoints);
        Assert.IsTrue(capturedEndpoints.ContainsKey(configuredSourceUrl));
        Assert.IsTrue(capturedEndpoints.ContainsKey("https://simulated.example/source"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Dependencies_RoutesToUnifiedTaskExecution()
    {
        var worker = CreateWorker();
        var job = CreateJob(JobKind.Dependencies);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-deps",
            CancellationToken.None);

        _planExecutor.Verify(executor => executor.DispatchTasksAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<ExportContext?>(),
            It.IsAny<ImportContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _planExecutor.Verify(executor => executor.ExportAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<ExportContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Import_RunsPrepareBeforeImportPhaseExecution()
    {
        var module = new Mock<IModule>();
        module.SetupGet(m => m.Name).Returns("WorkItems");
        module.SetupGet(m => m.DependsOn).Returns(Array.Empty<ModuleDependency>());
        module.SetupGet(m => m.SupportsInventory).Returns(false);
        module.SetupGet(m => m.SupportsExport).Returns(true);
        module.SetupGet(m => m.SupportsPrepare).Returns(true);
        module.SetupGet(m => m.SupportsImport).Returns(true);
        module.SetupGet(m => m.SupportsValidate).Returns(false);

        var sequence = new MockSequence();
        module
            .InSequence(sequence)
            .Setup(m => m.PrepareAsync(It.IsAny<PrepareContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaskExecutionResult.Completed());
        _planExecutor
            .InSequence(sequence)
            .Setup(executor => executor.ImportAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<ImportContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker([module.Object]);
        var job = CreateJob(JobKind.Import);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-import-order",
            CancellationToken.None);

        module.Verify(m => m.PrepareAsync(It.IsAny<PrepareContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _planExecutor.Verify(executor => executor.ImportAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<ImportContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Dependencies_EmitsJobReadyAfterPushingTaskList()
    {
        var progressEvents = new List<ProgressEvent>();
        _progressSink
            .Setup(sink => sink.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(evt => progressEvents.Add(evt));

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Dependencies);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-deps",
            CancellationToken.None);

        Assert.IsTrue(
            progressEvents.Any(evt => evt.Module == "Job" && evt.Stage == "Job.Ready"),
            "Dependencies jobs must emit Job.Ready after the plan is pushed so the CLI can fetch bootstrap.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Dependencies_ForceFresh_DeletesScopedCheckpointStateViaCheckpointer()
    {
        var worker = CreateWorker([new FakeModule("Dependencies", supportsPrepare: false)]);
        var job = new Job
        {
            JobId = "job-Dependencies",
            Kind = JobKind.Dependencies,
            ConfigPayload = "{\"MigrationPlatform\":{\"Package\":{\"WorkingDirectory\":\".\"}}}",
            Resume = new JobResume
            {
                Mode = ResumeMode.ForceFresh
            }
        };

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-deps-forcefresh",
            CancellationToken.None);

        _checkpointer.Verify(
            checkpointer => checkpointer.DeleteCursorAsync("Dependencies", It.IsAny<CancellationToken>()),
            Times.Once);

        _checkpointer.Verify(
            checkpointer => checkpointer.DeleteContinuationTokenAsync("Dependencies", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Inventory_WhenInventoryPlanSucceeds_WritesInventoryCompletionMarker()
    {
        string? inventoryMarkerPayload = null;
        _package
            .Setup(package => package.PersistMetaAsync(
                It.IsAny<PackageMetaContext>(),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback((PackageMetaContext context, PackageMetaPayload payload, CancellationToken _) =>
            {
                if (context.Kind != PackageMetaKind.InventoryCompletionMarker)
                    return;

                if (payload.Content.CanSeek)
                    payload.Content.Position = 0;
                using var reader = new System.IO.StreamReader(
                    payload.Content,
                    System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true);
                inventoryMarkerPayload = reader.ReadToEnd();
                if (payload.Content.CanSeek)
                    payload.Content.Position = 0;
            })
            .Returns(ValueTask.CompletedTask);

        _plan = new JobTaskList
        {
            Tasks = new List<JobTask>
            {
                new()
                {
                    Id = "capture.workitems.simulated.sourceproject",
                    Name = "WorkItems Capture",
                    TaskKind = TaskKind.Capture,
                    ProjectName = "SourceProject",
                    Order = 0,
                    Status = JobTaskStatus.Pending,
                    DependsOn = Array.Empty<string>()
                },
                new()
                {
                    Id = "analyse.inventory",
                    Name = "Inventory Analyse",
                    TaskKind = TaskKind.Analyse,
                    Order = 1,
                    Status = JobTaskStatus.Pending,
                    DependsOn = new[] { "capture.workitems.simulated.sourceproject" }
                }
            }.AsReadOnly()
        };

        _planBuilder
            .Setup(builder => builder.BuildAndSaveAsync(
                It.IsAny<IConfiguration>(),
                It.IsAny<JobKind>(),
                It.IsAny<IPackageAccess>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_plan);

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Inventory);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-inventory",
            CancellationToken.None);

        _package.Verify(
            package => package.PersistMetaAsync(
                It.Is<PackageMetaContext>(context =>
                    context.Kind == PackageMetaKind.InventoryCompletionMarker),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.IsNotNull(inventoryMarkerPayload);
        StringAssert.Contains(inventoryMarkerPayload, "\"phase\":\"Inventory\"");
        StringAssert.Contains(inventoryMarkerPayload, "\"jobId\":\"job-Inventory\"");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_Export_WhenExportPhaseFails_DoesNotWriteInventoryCompletionMarker()
    {
        _plan = new JobTaskList
        {
            Tasks = new List<JobTask>
            {
                new()
                {
                    Id = "analyse.inventory",
                    Name = "Inventory Analyse",
                    TaskKind = TaskKind.Analyse,
                    Order = 0,
                    Status = JobTaskStatus.Pending,
                    DependsOn = Array.Empty<string>()
                },
                new()
                {
                    Id = "export.workitems.simulated.sourceproject",
                    Name = "WorkItems Export",
                    TaskKind = TaskKind.Export,
                    Phase = "Export",
                    ProjectName = "SourceProject",
                    Order = 1,
                    Status = JobTaskStatus.Pending,
                    DependsOn = new[] { "analyse.inventory" }
                }
            }.AsReadOnly()
        };

        _planBuilder
            .Setup(builder => builder.BuildAndSaveAsync(
                It.IsAny<IConfiguration>(),
                It.IsAny<JobKind>(),
                It.IsAny<IPackageAccess>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_plan);

        _planExecutor
            .Setup(executor => executor.ExportAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
                It.IsAny<InventoryContext?>(),
                It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
                It.IsAny<ExportContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-export-fail",
            CancellationToken.None);

        _package.Verify(
            package => package.PersistMetaAsync(
                It.Is<PackageMetaContext>(context =>
                    context.Kind == PackageMetaKind.InventoryCompletionMarker),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_UnknownKind_FailsWithoutRunningPlanExecutor()
    {
        var worker = CreateWorker();
        var job = CreateJob((JobKind)999);

        _leaseState.CurrentLeaseId = "lease-unknown";
        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-unknown",
            CancellationToken.None);

        _planExecutor.Verify(executor => executor.ExportAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<ExportContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _planExecutor.Verify(executor => executor.DispatchTasksAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<ExportContext?>(),
            It.IsAny<ImportContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(_httpHandler.RequestLog.Exists(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.PathAndQuery.Contains("/events", StringComparison.OrdinalIgnoreCase) &&
            _httpHandler.RequestBodies.TryGetValue(request, out var body) &&
            ContainsTerminalEvent(body, failed: true)));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_WhenMigrationExecutionThrows_ClearsCurrentPackageConfigAccessor()
    {
        _planExecutor
            .Setup(executor => executor.ExportAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
                It.IsAny<InventoryContext?>(),
                It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
                It.IsAny<ExportContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated export failure"));

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        _leaseState.CurrentLeaseId = "lease-failure";
        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-failure",
            CancellationToken.None);

        _currentPackageConfigAccessor.Verify(accessor => accessor.Clear(), Times.AtLeastOnce);
        Assert.IsTrue(_httpHandler.RequestLog.Exists(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.PathAndQuery.Contains("/events", StringComparison.OrdinalIgnoreCase) &&
            _httpHandler.RequestBodies.TryGetValue(request, out var body) &&
            ContainsTerminalEvent(body, failed: true)));
    }

    // ── Scenarios: Agent fails fast when migration-config.json is absent ────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_WhenConfigAbsent_SignalsFailTerminal()
    {
        _packageMigrationConfigLoader
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DevOpsMigrationPlatform.Abstractions.Storage.PackageConfigNotFoundException("test-package"));

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        _leaseState.CurrentLeaseId = "lease-config-absent";
        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker, job, CreateControlPlaneClient(), "lease-config-absent", CancellationToken.None);

        Assert.IsTrue(_httpHandler.RequestLog.Exists(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.PathAndQuery.Contains("/events", StringComparison.OrdinalIgnoreCase) &&
            _httpHandler.RequestBodies.TryGetValue(request, out var body) &&
            ContainsTerminalEvent(body, failed: true)),
            "Expected job to be signaled as 'fail' when migration-config.json is absent.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_WhenConfigAbsent_DoesNotExecuteModules()
    {
        _packageMigrationConfigLoader
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DevOpsMigrationPlatform.Abstractions.Storage.PackageConfigNotFoundException("test-package"));

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker, job, CreateControlPlaneClient(), "lease-config-absent-no-modules", CancellationToken.None);

        _planExecutor.Verify(executor => executor.ExportAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<ExportContext>(),
            It.IsAny<CancellationToken>()), Times.Never,
            "No modules should execute when migration-config.json is absent.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_WhenConnectorConfigOmitsUrl_PopulatesCurrentEndpointAccessor()
    {
        _packageConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Project"] = "SourceProject",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Target:Project"] = "TargetProject",
                ["MigrationPlatform:Mode"] = "Export",
            })
            .Build();

        _packageMigrationConfigLoader
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_packageConfiguration);

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-no-url",
            CancellationToken.None);

        _currentJobEndpointAccessor.Verify(
            accessor => accessor.SetSource(It.Is<ISourceEndpointInfo>(endpoint =>
                endpoint.ConnectorType == "Simulated" &&
                endpoint.Project == "SourceProject" &&
                endpoint.Url == string.Empty)),
            Times.Once);

        _currentJobEndpointAccessor.Verify(
            accessor => accessor.SetTarget(It.Is<ITargetEndpointInfo>(endpoint =>
                endpoint.ConnectorType == "Simulated" &&
                endpoint.Project == "TargetProject" &&
                endpoint.Url == string.Empty)),
            Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task OnJobAsync_ForceFresh_DeletesInventoryCompletionMarker()
    {
        var worker = CreateWorker();
        var job = new Job
        {
            JobId = "job-Export",
            Kind = JobKind.Export,
            ConfigPayload = "{\"MigrationPlatform\":{\"Package\":{\"WorkingDirectory\":\".\"}}}",
            Resume = new JobResume
            {
                Mode = ResumeMode.ForceFresh
            }
        };

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-forcefresh",
            CancellationToken.None);
    }

    private JobAgentWorker CreateWorker(IReadOnlyList<IModule>? migrationModules = null)
    {
        var scopeFactory = _scopeFactory;
        if (migrationModules is { Count: > 0 })
        {
            var services = new ServiceCollection();
            foreach (var module in migrationModules)
                services.AddSingleton(module);
            services.AddSingleton<ISourceEndpointInfo>(new FakeSourceEndpointInfo());
            services.AddSingleton<ITargetEndpointInfo>(new FakeTargetEndpointInfo());
            services.AddSingleton<IAnalyser>(new FakeAnalyser("Dependencies"));
            services.AddSingleton<IJobExecutionPlanBuilder>(_planBuilder.Object);
            services.AddSingleton<IJobPlanExecutor>(_planExecutor.Object);
            scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        return new JobAgentWorker(
                        packagePreparer: _packagePreparer.Object,
            package: _package.Object,
            progressSink: _progressSink.Object,
            leaseState: _leaseState,
            packageState: _packageState,
            activeJobState: _activeJobState.Object,
            currentPackageConfigAccessor: _currentPackageConfigAccessor.Object,
            packageMigrationConfigLoader: _packageMigrationConfigLoader.Object,
            moduleScopeFactory: scopeFactory,
            httpClientFactory: new TestHttpClientFactory(CreateControlPlaneClient()),
            checkpointingFactory: _checkpointingFactory.Object,
            phaseTrackingFactory: _phaseTrackingFactory.Object,
            metricsStore: _metricsStore.Object,
            snapshotStore: _snapshotStore.Object,
            flushables: _flushables,
            currentJobContextAccessor: _currentJobContextAccessor.Object,
            currentJobEndpointAccessor: _currentJobEndpointAccessor.Object,
            eventWriter: _eventWriter,
            logger: _logger);
    }

    private Job CreateJob(JobKind kind)
    {
        return new Job
        {
            JobId = $"job-{kind}",
            Kind = kind,
            ConfigPayload = "{\"MigrationPlatform\":{\"Package\":{\"WorkingDirectory\":\".\"}}}",
        };
    }

    private HttpClient CreateControlPlaneClient() =>
        new(_httpHandler) { BaseAddress = new Uri("http://localhost:5100") };

    private sealed class FakeModule(string name, bool supportsPrepare) : IModule
    {
        public string Name => name;

        public IModuleContract Contract => new ModuleContract(Name, [], [], []);
        public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
        public bool SupportsInventory => false;
        public bool SupportsExport => true;
        public bool SupportsPrepare => supportsPrepare;
        public bool SupportsImport => true;
        public bool SupportsValidate => false;

        public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ValidateAsync(Abstractions.Agent.Validation.ValidationContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
    }

    private sealed class FakeAnalyser(string name) : IAnalyser
    {
        public string Name => name;

        public IModuleContract Contract => new ModuleContract(Name, [], [], []);
        public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
        public Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken cancellationToken) => Task.FromResult(TaskExecutionResult.Completed());
    }

    private sealed class FakeTargetEndpointInfo : ITargetEndpointInfo
    {
        public string Url => "https://simulated.example/target";
        public string Project => "TargetProject";
        public string ConnectorType => "Simulated";
        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            Type = ConnectorType,
            ResolvedUrl = Url,
        };
    }

    private sealed class FakeSourceEndpointInfo : ISourceEndpointInfo
    {
        public string Url => "https://simulated.example/source";
        public string Project => "SourceProject";
        public string ConnectorType => "Simulated";
        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            Type = ConnectorType,
            ResolvedUrl = Url,
        };
    }

    private sealed class TestHttpClientFactory(HttpClient controlPlaneClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => controlPlaneClient;
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> RequestLog { get; } = new();

        // The caller (HttpClient) disposes request Content once SendAsync returns, so callers
        // that inspect RequestLog after the fact (e.g. asserting on request bodies) must read
        // the content here, while it is still live, and stash it for later retrieval.
        public Dictionary<HttpRequestMessage, string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestLog.Add(request);
            if (request.Content is not null)
                RequestBodies[request] = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    /// <summary>
    /// Inspects a captured request body (a serialized WorkerEventBatch) for a Terminal event
    /// whose embedded PayloadJson reports the given failed flag. Parses via JsonDocument rather
    /// than string-Contains because PayloadJson is a JSON string nested inside the outer batch JSON.
    /// </summary>
    private static bool ContainsTerminalEvent(string body, bool failed)
    {
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("events", out var events))
            return false;

        foreach (var evt in events.EnumerateArray())
        {
            if (!evt.TryGetProperty("kind", out var kindProp))
                continue;
            if (!string.Equals(kindProp.GetString(), "Terminal", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!evt.TryGetProperty("payloadJson", out var payloadJsonProp))
                continue;

            var payloadJson = payloadJsonProp.GetString();
            if (payloadJson is null)
                continue;

            using var payloadDoc = JsonDocument.Parse(payloadJson);
            if (payloadDoc.RootElement.TryGetProperty("failed", out var failedProp) &&
                failedProp.GetBoolean() == failed)
                return true;
        }

        return false;
    }
}
