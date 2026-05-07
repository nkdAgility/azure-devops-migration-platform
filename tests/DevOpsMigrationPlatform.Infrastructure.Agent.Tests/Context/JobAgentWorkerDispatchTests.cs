// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
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
    private Mock<IPackageStoreFactory> _packageStoreFactory = null!;
    private Mock<IPackagePreparer> _packagePreparer = null!;
    private Mock<IProgressSink> _progressSink = null!;
    private Mock<ICheckpointingServiceFactory> _checkpointingFactory = null!;
    private Mock<IPhaseTrackingServiceFactory> _phaseTrackingFactory = null!;
    private Mock<IArtefactStore> _artefactStore = null!;
    private Mock<IStateStore> _stateStore = null!;
    private Mock<ICheckpointingService> _checkpointer = null!;
    private Mock<IPhaseTrackingService> _phaseTracker = null!;
    private Mock<IPackageConfigStore> _packageConfigStore = null!;
    private Mock<IJobExecutionPlanBuilder> _planBuilder = null!;
    private Mock<IJobPlanExecutor> _planExecutor = null!;
    private Mock<ICurrentPackageConfigAccessor> _currentPackageConfigAccessor = null!;
    private Mock<ICurrentAgentJobContextAccessor> _currentJobContextAccessor = null!;
    private Mock<ICurrentJobEndpointAccessor> _currentJobEndpointAccessor = null!;
    private Mock<IControlPlaneTelemetryClient> _telemetryClient = null!;
    private Mock<IJobMetricsStore> _metricsStore = null!;
    private Mock<IJobSnapshotStore> _snapshotStore = null!;
    private Mock<IActiveJobState> _activeJobState = null!;
    private MockHttpMessageHandler _httpHandler = null!;
    private ActiveLeaseState _leaseState = null!;
    private ActivePackageState _packageState = null!;
    private JobConfiguration _jobConfiguration = null!;
    private IConfiguration _packageConfiguration = null!;
    private JobTaskList _plan = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private ILogger<JobAgentWorker> _logger = null!;
    private IFlushable[] _flushables = null!;

    [TestInitialize]
    public void Setup()
    {
        _packageStoreFactory = new Mock<IPackageStoreFactory>();
        _packagePreparer = new Mock<IPackagePreparer>();
        _progressSink = new Mock<IProgressSink>();
        _checkpointingFactory = new Mock<ICheckpointingServiceFactory>();
        _phaseTrackingFactory = new Mock<IPhaseTrackingServiceFactory>();
        _artefactStore = new Mock<IArtefactStore>();
        _stateStore = new Mock<IStateStore>();
        _checkpointer = new Mock<ICheckpointingService>();
        _phaseTracker = new Mock<IPhaseTrackingService>();
        _packageConfigStore = new Mock<IPackageConfigStore>();
        _planBuilder = new Mock<IJobExecutionPlanBuilder>();
        _planExecutor = new Mock<IJobPlanExecutor>();
        _currentPackageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>();
        _currentJobContextAccessor = new Mock<ICurrentAgentJobContextAccessor>();
        _currentJobEndpointAccessor = new Mock<ICurrentJobEndpointAccessor>();
        _telemetryClient = new Mock<IControlPlaneTelemetryClient>();
        _metricsStore = new Mock<IJobMetricsStore>();
        _snapshotStore = new Mock<IJobSnapshotStore>();
        _activeJobState = new Mock<IActiveJobState>();
        _httpHandler = new MockHttpMessageHandler();
        _leaseState = new ActiveLeaseState();
        _packageState = new ActivePackageState();
        _jobConfiguration = new JobConfiguration();
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

        _packageStoreFactory
            .Setup(factory => factory.Create(It.IsAny<string>()))
            .Returns((_artefactStore.Object, _stateStore.Object));

        _packagePreparer
            .Setup(preparer => preparer.PrepareForImportAsync(It.IsAny<IArtefactStore>(), It.IsAny<IConfiguration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _checkpointingFactory
            .Setup(factory => factory.Create(It.IsAny<IStateStore>()))
            .Returns(_checkpointer.Object);

        _phaseTrackingFactory
            .Setup(factory => factory.Create(It.IsAny<IStateStore>()))
            .Returns(_phaseTracker.Object);

        _phaseTracker
            .Setup(tracker => tracker.ReadPhaseRecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPhaseRecord());

        _packageConfigStore
            .Setup(store => store.ReadAsync(It.IsAny<IArtefactStore>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_packageConfiguration);

        _artefactStore
            .Setup(store => store.ReadAsync(PackagePaths.MigrationConfigFileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
            {
              "MigrationPlatform": {
                "Source": { "Type": "Simulated", "Url": "https://simulated.example/source", "Project": "SourceProject" },
                "Organisations": [
                  {
                    "Enabled": true,
                    "Type": "Simulated",
                    "Url": "https://simulated.example/source",
                    "Projects": ["SourceProject"],
                    "Scopes": []
                  }
                ]
              }
            }
            """);

        _planBuilder
            .Setup(builder => builder.BuildAndSaveAsync(
                It.IsAny<IConfiguration>(),
                It.IsAny<JobKind>(),
                It.IsAny<IArtefactStore>(),
                It.IsAny<IStateStore>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_plan);

        _planExecutor
            .Setup(executor => executor.ExecuteExportPhaseAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<ExportContext>(),
                It.IsAny<IStateStore>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _planExecutor
            .Setup(executor => executor.ExecuteImportPhaseAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<ImportContext>(),
                It.IsAny<IStateStore>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _planExecutor
            .Setup(executor => executor.ExecuteTasksAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
                It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
                It.IsAny<InventoryContext?>(),
                It.IsAny<ExportContext?>(),
                It.IsAny<ImportContext?>(),
                It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
                It.IsAny<IStateStore>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _telemetryClient
            .Setup(client => client.PushTaskListAsync(It.IsAny<string>(), It.IsAny<JobTaskList>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _flushables =
        [
            new PackageProgressSink(_packageState, NullLogger<PackageProgressSink>.Instance),
            new PackageLoggerProvider(_packageState, Options.Create(new DiagnosticLogOptions())),
        ];

        var services = new ServiceCollection();
        services.AddSingleton<IModule>(new FakeModule("WorkItems", supportsPrepare: true));
        services.AddSingleton<ITargetEndpointInfo>(new FakeTargetEndpointInfo());
        services.AddSingleton<IAnalyser>(new FakeAnalyser("Dependencies"));
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

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

        _planExecutor.Verify(executor => executor.ExecuteExportPhaseAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<ExportContext>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _planExecutor.Verify(executor => executor.ExecuteTasksAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<ExportContext?>(),
            It.IsAny<ImportContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

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

        _planExecutor.Verify(executor => executor.ExecuteTasksAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<ExportContext?>(),
            It.IsAny<ImportContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _planExecutor.Verify(executor => executor.ExecuteExportPhaseAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<ExportContext>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task OnJobAsync_UnknownKind_FailsWithoutRunningPlanExecutor()
    {
        var worker = CreateWorker();
        var job = CreateJob((JobKind)999);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-unknown",
            CancellationToken.None);

        _planExecutor.Verify(executor => executor.ExecuteExportPhaseAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, IModule>>(),
            It.IsAny<ExportContext>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _planExecutor.Verify(executor => executor.ExecuteTasksAsync(
            It.IsAny<JobTaskList>(),
            It.IsAny<IReadOnlyDictionary<string, ICapture>>(),
            It.IsAny<IReadOnlyDictionary<string, IAnalyser>>(),
            It.IsAny<InventoryContext?>(),
            It.IsAny<ExportContext?>(),
            It.IsAny<ImportContext?>(),
            It.IsAny<IReadOnlyDictionary<string, OrganisationEndpoint>?>(),
            It.IsAny<IStateStore>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(_httpHandler.RequestLog.Exists(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.PathAndQuery.Contains("/fail", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task OnJobAsync_WhenMigrationExecutionThrows_ClearsActiveJobConfig()
    {
        _planExecutor
            .Setup(executor => executor.ExecuteExportPhaseAsync(
                It.IsAny<JobTaskList>(),
                It.IsAny<IReadOnlyDictionary<string, IModule>>(),
                It.IsAny<ExportContext>(),
                It.IsAny<IStateStore>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated export failure"));

        var worker = CreateWorker();
        var job = CreateJob(JobKind.Export);

        await JobAgentWorkerTestHelper.InvokeJobAsync(
            worker,
            job,
            CreateControlPlaneClient(),
            "lease-failure",
            CancellationToken.None);

        Assert.IsNull(_jobConfiguration.PackageConfig);
        Assert.IsTrue(_httpHandler.RequestLog.Exists(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.PathAndQuery.Contains("/fail", StringComparison.OrdinalIgnoreCase)));
    }

    private JobAgentWorker CreateWorker()
    {
        return new JobAgentWorker(
            migrationModules: Array.Empty<IModule>(),
            packageStoreFactory: _packageStoreFactory.Object,
            packagePreparer: _packagePreparer.Object,
            progressSink: _progressSink.Object,
            leaseState: _leaseState,
            packageState: _packageState,
            activeJobConfig: _jobConfiguration,
            activeJobState: _activeJobState.Object,
            currentPackageConfigAccessor: _currentPackageConfigAccessor.Object,
            packageConfigStore: _packageConfigStore.Object,
            moduleScopeFactory: _scopeFactory,
            httpClientFactory: new TestHttpClientFactory(CreateControlPlaneClient()),
            checkpointingFactory: _checkpointingFactory.Object,
            phaseTrackingFactory: _phaseTrackingFactory.Object,
            metricsStore: _metricsStore.Object,
            snapshotStore: _snapshotStore.Object,
            flushables: _flushables,
            planBuilder: _planBuilder.Object,
            planExecutor: _planExecutor.Object,
            currentJobContextAccessor: _currentJobContextAccessor.Object,
            currentJobEndpointAccessor: _currentJobEndpointAccessor.Object,
            telemetryClient: _telemetryClient.Object,
            logger: _logger);
    }

    private Job CreateJob(JobKind kind)
    {
        return new Job
        {
            JobId = $"job-{kind}",
            Kind = kind,
            Package = new JobPackage { PackageUri = "." },
        };
    }

    private HttpClient CreateControlPlaneClient() =>
        new(_httpHandler) { BaseAddress = new Uri("http://localhost:5100") };

    private sealed class FakeModule(string name, bool supportsPrepare) : IModule
    {
        public string Name => name;
        public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
        public bool SupportsInventory => false;
        public bool SupportsExport => true;
        public bool SupportsPrepare => supportsPrepare;
        public bool SupportsImport => true;
        public bool SupportsValidate => false;

        public Task CaptureAsync(InventoryContext context, CancellationToken ct) => Task.CompletedTask;
        public Task ExportAsync(ExportContext context, CancellationToken ct) => Task.CompletedTask;
        public Task PrepareAsync(PrepareContext context, CancellationToken ct) => Task.CompletedTask;
        public Task ImportAsync(ImportContext context, CancellationToken ct) => Task.CompletedTask;
        public Task ValidateAsync(Abstractions.Agent.Validation.ValidationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAnalyser(string name) : IAnalyser
    {
        public string Name => name;
        public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
        public Task AnalyseAsync(AnalyseContext context, CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed class TestHttpClientFactory(HttpClient controlPlaneClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => controlPlaneClient;
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> RequestLog { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestLog.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}