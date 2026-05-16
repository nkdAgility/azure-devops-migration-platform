// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.Extensions.DependencyInjection;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// Helper that invokes protected methods on the sealed TfsJobAgentWorker via reflection.
/// </summary>
internal static class TfsJobAgentWorkerTestHelper
{
    private static readonly System.Reflection.MethodInfo OnJobMethod =
        typeof(TfsJobAgentWorker).GetMethod(
            "OnJobAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private static readonly System.Reflection.MethodInfo OnDiscoveryJobMethod =
        typeof(TfsJobAgentWorker).GetMethod(
            "OnDiscoveryJobAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    public static Task InvokeMigrationJobAsync(
        TfsJobAgentWorker worker, Job job, HttpClient client, string leaseId, CancellationToken ct)
    {
        return (Task)OnJobMethod.Invoke(worker, new object[] { job, client, leaseId, ct })!;
    }

    public static Task InvokeDiscoveryJobAsync(
        TfsJobAgentWorker worker, Job job, HttpClient client, string leaseId, CancellationToken ct)
    {
        return (Task)OnDiscoveryJobMethod.Invoke(worker, new object[] { job, client, leaseId, ct })!;
    }
}

[TestClass]
[TestCategory("NET481")]
public class TfsJobAgentWorkerTests
{
    private Mock<IProgressSink> _progressSink = null!;
    private Mock<ICheckpointingServiceFactory> _checkpointingFactory = null!;
    private Mock<IPhaseTrackingServiceFactory> _phaseTrackingFactory = null!;
    private Mock<ITfsJobServiceFactory> _tfsServiceFactory = null!;
    private Mock<IPackageAccess> _package = null!;
    private Mock<ICheckpointingService> _checkpointer = null!;
    private Mock<IPackageMigrationConfigLoader> _packageMigrationConfigLoader = null!;
    private Mock<IActiveJobState> _activeJobState = null!;
    private ActiveLeaseState _leaseState = null!;
    private ActivePackageState _packageState = null!;
    private IFlushable[] _flushables = null!;
    private MockHttpMessageHandler _httpHandler = null!;
    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private ILogger<TfsJobAgentWorker> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _progressSink = new Mock<IProgressSink>();
        _checkpointingFactory = new Mock<ICheckpointingServiceFactory>();
        _phaseTrackingFactory = new Mock<IPhaseTrackingServiceFactory>();
        _tfsServiceFactory = new Mock<ITfsJobServiceFactory>();
        _package = new Mock<IPackageAccess>(MockBehavior.Loose);
        _checkpointer = new Mock<ICheckpointingService>();
        _leaseState = new ActiveLeaseState();
        _packageState = new ActivePackageState();
        _logger = NullLogger<TfsJobAgentWorker>.Instance;

        _flushables = new IFlushable[]
        {
            new PackageProgressSink(_packageState, NullLogger<PackageProgressSink>.Instance, _package.Object),
            new PackageLoggerProvider(_packageState, Options.Create(new DiagnosticLogOptions()), _package.Object),
        };

        _checkpointingFactory
            .Setup(f => f.Create(It.IsAny<IPackageAccess>()))
            .Returns(_checkpointer.Object);

        _phaseTrackingFactory
            .Setup(f => f.Create(It.IsAny<IPackageAccess>()))
            .Returns(Mock.Of<IPhaseTrackingService>());

        _checkpointer
            .Setup(c => c.ReadCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);

        _checkpointer
            .Setup(c => c.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _checkpointer
            .Setup(c => c.DeleteCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _httpHandler = new MockHttpMessageHandler();
        _httpClientFactory = new Mock<IHttpClientFactory>();
        _httpClientFactory
            .Setup(f => f.CreateClient("ControlPlane"))
            .Returns(new HttpClient(_httpHandler)
            {
                BaseAddress = new Uri("http://localhost:5100")
            });

        // Package migration config loader — default returns a config with a TFS source.
        _packageMigrationConfigLoader = new Mock<IPackageMigrationConfigLoader>();
        _activeJobState = new Mock<IActiveJobState>();
        _packageMigrationConfigLoader
            .Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildTfsSourceConfig(CreateTfsEndpoint()));
    }

    private TfsJobAgentWorker CreateWorker(
        IEnumerable<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>? modules = null,
        IPackageAccess? package = null)
    {
        // Build a minimal ServiceProvider that returns the caller-supplied modules
        // from any IServiceScope so per-job scope resolution works in tests.
        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var m in modules ?? System.Linq.Enumerable.Empty<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>())
            sc.AddSingleton(m);
        var sp = sc.BuildServiceProvider();

        return new(
            _progressSink.Object,
            _leaseState,
            _packageState,
            _activeJobState.Object,
            new CurrentPackageConfigAccessor(),
            _packageMigrationConfigLoader.Object,
            sp.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            _httpClientFactory.Object,
            _checkpointingFactory.Object,
            _phaseTrackingFactory.Object,
            _flushables,
            _tfsServiceFactory.Object,
            new DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ActiveTfsJobServices(),
            _logger,
            package ?? _package.Object);
    }

    private HttpClient CreateControlPlaneClient() =>
        new(_httpHandler) { BaseAddress = new Uri("http://localhost:5100") };

    // ── Export Tests ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnMigrationJob_NullSource_SignalsFail()
    {
        // Arrange: package config has no Source — worker should fail during OnBeforeModulesAsync.
        _packageMigrationConfigLoader
            .Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEmptyConfig()); // no Source

        var job = new Job
        {
            JobId = "test-job-1",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "." }
        };

        var worker = CreateWorker();

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert: should have posted /agents/lease/{leaseId}/fail
        Assert.IsTrue(_httpHandler.RequestLog.Exists(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.PathAndQuery.Contains("/fail")));

        _tfsServiceFactory.Verify(
            f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()), Times.Never);
    }

    [TestMethod]
    public async Task OnMigrationJob_NonExportMode_SignalsFail()
    {
        // Arrange
        var job = new Job
        {
            JobId = "test-job-2",
            Kind = JobKind.Import,
            Package = new JobPackage { PackageUri = "." }
        };

        var worker = CreateWorker();

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert
        Assert.IsTrue(_httpHandler.RequestLog.Exists(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.PathAndQuery.Contains("/fail")));

        _tfsServiceFactory.Verify(
            f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()), Times.Never);
    }

    [TestMethod]
    public async Task OnMigrationJob_ExportMode_CreatesServicesAndSignalsComplete()
    {
        // Arrange
        var endpoint = CreateTfsEndpoint();
        var job = new Job
        {
            JobId = "test-job-3",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "." }
        };

        var mockRevisionSource = new Mock<IWorkItemRevisionSource>();
        mockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<WorkItemRevision>());

        var mockAttachmentSource = new Mock<IAttachmentBinarySource>();
        var mockTreeReader = new Mock<IClassificationTreeReader>();
        mockTreeReader
            .Setup(r => r.EnumerateAreaNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<string>());
        mockTreeReader
            .Setup(r => r.EnumerateIterationNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<IterationNodeEntry>());

        var mockDiscovery = new Mock<IWorkItemDiscoveryService>();
        mockDiscovery
            .Setup(d => d.CountWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<ProjectDiscoverySummary>());

        var tfsServices = TestTfsJobServicesFactory.Create(
            mockRevisionSource.Object,
            mockAttachmentSource.Object,
            mockTreeReader.Object,
            mockDiscovery.Object);

        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Returns(tfsServices);

        var worker = CreateWorker();

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert: factory was called
        _tfsServiceFactory.Verify(
            f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()), Times.Once);

        // Should signal complete (not fail)
        Assert.IsTrue(_httpHandler.RequestLog.Exists(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.PathAndQuery.Contains("/complete")));
    }

    [TestMethod]
    public async Task OnMigrationJob_ExportMode_UsesPackageBoundaryForPlanStatusUpdates()
    {
        var job = new Job
        {
            JobId = "test-job-pkg-plan",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "." }
        };

        var mockRevisionSource = new Mock<IWorkItemRevisionSource>();
        mockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<WorkItemRevision>());

        var mockAttachmentSource = new Mock<IAttachmentBinarySource>();
        var mockTreeReader = new Mock<IClassificationTreeReader>();
        mockTreeReader
            .Setup(r => r.EnumerateAreaNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<string>());
        mockTreeReader
            .Setup(r => r.EnumerateIterationNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<IterationNodeEntry>());

        var mockDiscovery = new Mock<IWorkItemDiscoveryService>();
        mockDiscovery
            .Setup(d => d.CountWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<ProjectDiscoverySummary>());

        var tfsServices = TestTfsJobServicesFactory.Create(
            mockRevisionSource.Object,
            mockAttachmentSource.Object,
            mockTreeReader.Object,
            mockDiscovery.Object);
        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Returns(tfsServices);

        var planJson = "{\"Tasks\":[{\"Id\":\"export.workitems\",\"Name\":\"Work Items Export\",\"TaskKind\":1,\"Order\":0,\"Status\":0}],\"Phases\":[],\"ForKind\":2}";
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/plan.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes(planJson)), "application/json"))));
        package
            .Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        var worker = CreateWorker(package: package.Object);
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        package.Verify(p => p.RequestMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        package.Verify(p => p.PersistMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
            It.IsAny<PackageMetaPayload>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task OnMigrationJob_ExportMode_DoesNotFallbackToStateStoreWhenPackagePlanMissing()
    {
        var job = new Job
        {
            JobId = "test-job-state-plan",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "." }
        };

        var mockRevisionSource = new Mock<IWorkItemRevisionSource>();
        mockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<WorkItemRevision>());

        var mockAttachmentSource = new Mock<IAttachmentBinarySource>();
        var mockTreeReader = new Mock<IClassificationTreeReader>();
        mockTreeReader
            .Setup(r => r.EnumerateAreaNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<string>());
        mockTreeReader
            .Setup(r => r.EnumerateIterationNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<IterationNodeEntry>());

        var mockDiscovery = new Mock<IWorkItemDiscoveryService>();
        mockDiscovery
            .Setup(d => d.CountWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<ProjectDiscoverySummary>());

        var tfsServices = TestTfsJobServicesFactory.Create(
            mockRevisionSource.Object,
            mockAttachmentSource.Object,
            mockTreeReader.Object,
            mockDiscovery.Object);
        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Returns(tfsServices);

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/plan.json", null)));

        var worker = CreateWorker(package: package.Object);
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        package.Verify(p => p.RequestMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        package.Verify(p => p.PersistMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ExecutionPlan),
            It.IsAny<PackageMetaPayload>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task OnMigrationJob_ForceFresh_DeletesCursor()
    {
        // Arrange
        var endpoint = CreateTfsEndpoint();
        var job = new Job
        {
            JobId = "test-job-4",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "." },
            Resume = new JobResume { Mode = ResumeMode.ForceFresh }
        };

        var mockRevisionSource = new Mock<IWorkItemRevisionSource>();
        mockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<WorkItemRevision>());

        var mockTreeReader = new Mock<IClassificationTreeReader>();
        mockTreeReader
            .Setup(r => r.EnumerateAreaNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<string>());
        mockTreeReader
            .Setup(r => r.EnumerateIterationNodesAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<IterationNodeEntry>());

        var tfsServices = TestTfsJobServicesFactory.Create(
            mockRevisionSource.Object,
            new Mock<IAttachmentBinarySource>().Object,
            mockTreeReader.Object,
            new Mock<IWorkItemDiscoveryService>().Object);

        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Returns(tfsServices);

        _checkpointer
            .Setup(c => c.DeleteCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Provide a named module so ForceFresh has a cursor to delete.
        var moduleA = new Mock<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>();
        moduleA.Setup(m => m.Name).Returns("WorkItems");
        moduleA.Setup(m => m.DependsOn).Returns(System.Array.Empty<DevOpsMigrationPlatform.Abstractions.Agent.Modules.ModuleDependency>());
        moduleA.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(DevOpsMigrationPlatform.Abstractions.Agent.Modules.TaskExecutionResult.Completed());

        var worker = CreateWorker(new[] { moduleA.Object });

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert: cursor was deleted for the registered module
        _checkpointer.Verify(
            c => c.DeleteCursorAsync("WorkItems", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task OnMigrationJob_FactoryThrows_SignalsFail()
    {
        // Arrange
        var job = new Job
        {
            JobId = "test-job-5",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "." }
        };

        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Throws(new InvalidOperationException("TFS connection failed"));

        var worker = CreateWorker();

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeMigrationJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert
        Assert.IsTrue(_httpHandler.RequestLog.Exists(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.PathAndQuery.Contains("/fail")));
    }

    // ── Discovery Tests ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnDiscoveryJob_NullEndpoint_SignalsFail()
    {
        // Arrange
        var job = new Job
        {
            JobId = "disc-job-1",
            Kind = JobKind.Inventory,
            Package = new JobPackage { PackageUri = "." }
        };

        var worker = CreateWorker();

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeDiscoveryJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert
        Assert.IsTrue(_httpHandler.RequestLog.Exists(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.PathAndQuery.Contains("/fail")));
    }

    [TestMethod]
    public async Task OnDiscoveryJob_WithSource_StreamsCountsAndSignalsComplete()
    {
        // Arrange
        var endpoint = CreateTfsEndpoint();
        var job = new Job
        {
            JobId = "disc-job-2",
            Kind = JobKind.Inventory,
            Package = new JobPackage { PackageUri = "." }
        };

        var mockDiscovery = new Mock<IWorkItemDiscoveryService>();
        mockDiscovery
            .Setup(d => d.CountWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateDiscoverySummaryStream(
                new ProjectDiscoverySummary { WorkItemsCount = 10, RevisionsCount = 50, IsWorkItemComplete = false },
                new ProjectDiscoverySummary { WorkItemsCount = 10, RevisionsCount = 50, IsWorkItemComplete = true }));

        var tfsServices = TestTfsJobServicesFactory.Create(
            new Mock<IWorkItemRevisionSource>().Object,
            new Mock<IAttachmentBinarySource>().Object,
            new Mock<IClassificationTreeReader>().Object,
            mockDiscovery.Object);

        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Returns(tfsServices);

        var worker = CreateWorker();

        // Act
        await TfsJobAgentWorkerTestHelper.InvokeDiscoveryJobAsync(
            worker, job, CreateControlPlaneClient(), "test-lease", CancellationToken.None);

        // Assert: progress events were emitted
        _progressSink.Verify(
            p => p.Emit(It.Is<ProgressEvent>(e => e.Module == "Discovery")),
            Times.AtLeast(2));

        // Should signal complete
        Assert.IsTrue(_httpHandler.RequestLog.Exists(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.PathAndQuery.Contains("/complete")));
    }

    [TestMethod]
    public void Capabilities_ReturnsTfs()
    {
        var worker = CreateWorker();
        // Use reflection to verify — Capabilities is protected
        var prop = typeof(TfsJobAgentWorker)
            .GetProperty("Capabilities",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var caps = (DevOpsMigrationPlatform.Abstractions.Jobs.ConnectorType[])prop!.GetValue(worker)!;

        CollectionAssert.AreEqual(new[] { DevOpsMigrationPlatform.Abstractions.Jobs.ConnectorType.TeamFoundationServer }, caps);
    }

    // ── T031: O-1 Activity spans on net481 path ───────────────────────────────
    // Exercises PackageMigrationConfigLoader directly (InternalsVisibleTo granted) to confirm
    // Activity spans fire under the net481 runtime.

    [TestMethod]
    public async Task PackageMigrationConfigLoader_LoadAsync_EmitsConfigReadSpan_Net481()
    {
        var captured = new System.Collections.Generic.List<string>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = src => src.Name == DevOpsMigrationPlatform.Abstractions.WellKnownActivitySourceNames.Migration,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => captured.Add(a.OperationName)
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"MigrationPlatform\":{\"Mode\":\"Export\"}}"))))));

        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Loose);
        var sut = new DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem.PackageMigrationConfigLoader(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem.PackageMigrationConfigLoader>.Instance,
            package.Object,
            metrics.Object);

        var result = await sut.LoadAsync(CancellationToken.None);

        Assert.IsTrue(captured.Contains("config.read"),
            $"Expected 'config.read' span on net481. Got: [{string.Join(", ", captured)}]");
        Assert.IsNotNull(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IConfiguration BuildTfsSourceConfig(TeamFoundationServerEndpointOptions source)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = source.Type,
                ["MigrationPlatform:Source:Url"] = source.Url,
                ["MigrationPlatform:Source:Project"] = source.Project,
                ["MigrationPlatform:Source:Authentication:Type"] = source.Authentication?.Type.ToString(),
                ["MigrationPlatform:Source:Authentication:AccessToken"] = source.Authentication?.AccessToken,
            })
            .Build();

    private static IConfiguration BuildEmptyConfig()
        => new ConfigurationBuilder().Build();

    private static TeamFoundationServerEndpointOptions CreateTfsEndpoint() => new()
    {
        Url = "http://tfs:8080/tfs/DefaultCollection",
        Project = "TestProject",
        Type = "TeamFoundationServer",
        Authentication = new EndpointAuthenticationOptions
        {
            Type = AuthenticationType.Pat,
            AccessToken = "test-pat-token"
        }
    };

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> CreateDiscoverySummaryStream(
        params ProjectDiscoverySummary[] summaries)
    {
        foreach (var s in summaries)
        {
            await Task.CompletedTask;
            yield return s;
        }
    }
}

/// <summary>
/// Helper to create TfsJobServices for testing with a disposable-safe collection.
/// </summary>
internal static class TestTfsJobServicesFactory
{
    public static TfsJobServices Create(
        IWorkItemRevisionSource revisionSource,
        IAttachmentBinarySource attachmentSource,
        IClassificationTreeReader classificationTreeReader,
        IWorkItemDiscoveryService discoveryService)
    {
        // Use a real TfsTeamProjectCollection with a fake URI — Dispose is safe.
        var collection = new Microsoft.TeamFoundation.Client.TfsTeamProjectCollection(
            new Uri("http://fake:8080/tfs/DefaultCollection"));

        return new TfsJobServices(
            collection,
            null!,
            revisionSource,
            attachmentSource,
            new Mock<INodeCreator>().Object,
            classificationTreeReader,
            discoveryService,
            null!,
            null!,
            new TeamFoundationServerEndpointOptions
            {
                Url = "http://test:8080/tfs/DefaultCollection",
                Project = "TestProject",
                Type = "TeamFoundationServer"
            },
            new Mock<IWorkItemExportMetrics>().Object,
            new Mock<IAttachmentDownloadMetrics>().Object,
            new Mock<DevOpsMigrationPlatform.Abstractions.Agent.Tools.IIdentitySource>().Object);
    }
}

/// <summary>
/// Simple mock HTTP message handler that records requests and returns configured responses.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, HttpResponseMessage> _responses = new(StringComparer.OrdinalIgnoreCase);
    private HttpResponseMessage _defaultResponse = new(HttpStatusCode.OK);
    public List<HttpRequestMessage> RequestLog { get; } = new();

    public void SetResponse(string pathPrefix, HttpResponseMessage response) =>
        _responses[pathPrefix] = response;

    public void SetDefaultResponse(HttpResponseMessage response) =>
        _defaultResponse = response;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestLog.Add(request);
        return Task.FromResult(_defaultResponse);
    }
}
