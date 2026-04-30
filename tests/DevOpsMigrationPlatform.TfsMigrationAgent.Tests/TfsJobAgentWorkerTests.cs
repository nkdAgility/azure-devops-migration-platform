using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
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
public class TfsJobAgentWorkerTests
{
    private Mock<IPackageStoreFactory> _packageStoreFactory = null!;
    private Mock<IProgressSink> _progressSink = null!;
    private Mock<ICheckpointingServiceFactory> _checkpointingFactory = null!;
    private Mock<IPhaseTrackingServiceFactory> _phaseTrackingFactory = null!;
    private Mock<ITfsJobServiceFactory> _tfsServiceFactory = null!;
    private Mock<IArtefactStore> _artefactStore = null!;
    private Mock<IStateStore> _stateStore = null!;
    private Mock<ICheckpointingService> _checkpointer = null!;
    private Mock<IPackageConfigStore> _packageConfigStore = null!;
    private ActiveLeaseState _leaseState = null!;
    private ActivePackageState _packageState = null!;
    private IFlushable[] _flushables = null!;
    private MockHttpMessageHandler _httpHandler = null!;
    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private ILogger<TfsJobAgentWorker> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _packageStoreFactory = new Mock<IPackageStoreFactory>();
        _progressSink = new Mock<IProgressSink>();
        _checkpointingFactory = new Mock<ICheckpointingServiceFactory>();
        _phaseTrackingFactory = new Mock<IPhaseTrackingServiceFactory>();
        _tfsServiceFactory = new Mock<ITfsJobServiceFactory>();
        _artefactStore = new Mock<IArtefactStore>();
        _stateStore = new Mock<IStateStore>();
        _checkpointer = new Mock<ICheckpointingService>();
        _leaseState = new ActiveLeaseState();
        _packageState = new ActivePackageState();
        _logger = NullLogger<TfsJobAgentWorker>.Instance;

        _flushables = new IFlushable[]
        {
            new PackageProgressSink(_packageState, NullLogger<PackageProgressSink>.Instance),
            new PackageLoggerProvider(_packageState, Options.Create(new DiagnosticLogOptions())),
        };

        _packageStoreFactory
            .Setup(f => f.Create(It.IsAny<string>()))
            .Returns((_artefactStore.Object, _stateStore.Object));

        _checkpointingFactory
            .Setup(f => f.Create(It.IsAny<IStateStore>()))
            .Returns(_checkpointer.Object);

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

        // Package config store — default returns a config with a TFS source.
        _packageConfigStore = new Mock<IPackageConfigStore>();
        _packageConfigStore
            .Setup(s => s.ReadAsync(It.IsAny<IArtefactStore>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildTfsSourceConfig(CreateTfsEndpoint()));
    }

    private TfsJobAgentWorker CreateWorker(
        IEnumerable<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>? modules = null)
    {
        // Build a minimal ServiceProvider that returns the caller-supplied modules
        // from any IServiceScope so per-job scope resolution works in tests.
        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var m in modules ?? System.Linq.Enumerable.Empty<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>())
            sc.AddSingleton(m);
        var sp = sc.BuildServiceProvider();

        return new(
            modules ?? System.Linq.Enumerable.Empty<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>(),
            _packageStoreFactory.Object,
            _progressSink.Object,
            _leaseState,
            _packageState,
            new ActiveJobConfigState(),
            _packageConfigStore.Object,
            sp.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            _httpClientFactory.Object,
            _checkpointingFactory.Object,
            _phaseTrackingFactory.Object,
            _flushables,
            _tfsServiceFactory.Object,
            new DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ActiveTfsJobServices(),
            _logger);
    }

    private HttpClient CreateControlPlaneClient() =>
        new(_httpHandler) { BaseAddress = new Uri("http://localhost:5100") };

    // ── Export Tests ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnMigrationJob_NullSource_SignalsFail()
    {
        // Arrange: package config has no Source — worker should fail during OnBeforeModulesAsync.
        _packageConfigStore
            .Setup(s => s.ReadAsync(It.IsAny<IArtefactStore>(), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.EnumerateAreaNodesAsync(It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<string>());
        mockTreeReader
            .Setup(r => r.EnumerateIterationNodesAsync(It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<IterationNodeEntry>());

        var mockDiscovery = new Mock<IWorkItemDiscoveryService>();
        mockDiscovery
            .Setup(d => d.CountWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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

        _artefactStore
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
            .Setup(r => r.EnumerateAreaNodesAsync(It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<string>());
        mockTreeReader
            .Setup(r => r.EnumerateIterationNodesAsync(It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<IterationNodeEntry>());

        var tfsServices = TestTfsJobServicesFactory.Create(
            mockRevisionSource.Object,
            new Mock<IAttachmentBinarySource>().Object,
            mockTreeReader.Object,
            new Mock<IWorkItemDiscoveryService>().Object);

        _tfsServiceFactory
            .Setup(f => f.CreateForEndpoint(It.IsAny<MigrationEndpointOptions>()))
            .Returns(tfsServices);

        _artefactStore
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _checkpointer
            .Setup(c => c.DeleteCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Provide a named module so ForceFresh has a cursor to delete.
        var moduleA = new Mock<DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule>();
        moduleA.Setup(m => m.Name).Returns("WorkItems");
        moduleA.Setup(m => m.DependsOn).Returns(System.Array.Empty<string>());
        moduleA.Setup(m => m.ExportAsync(It.IsAny<ExportContext>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

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
    // Exercises PackageConfigStore directly (InternalsVisibleTo granted) to confirm
    // Activity spans fire under the net481 runtime.

    [TestMethod]
    public async Task PackageConfigStore_ReadAsync_EmitsConfigReadSpan_Net481()
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

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"MigrationPlatform\":{\"Mode\":\"Export\"}}");

        var factory = new Mock<IPackageStoreFactory>(MockBehavior.Loose);
        factory.Setup(f => f.Create(It.IsAny<string>()))
            .Returns((store.Object, new Mock<IStateStore>().Object));

        var metrics = new Mock<IMigrationMetrics>(MockBehavior.Loose);
        var sut = new DevOpsMigrationPlatform.Infrastructure.Agent.Storage.PackageConfigStore(
            factory.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DevOpsMigrationPlatform.Infrastructure.Agent.Storage.PackageConfigStore>.Instance,
            metrics.Object);

        var result = await sut.ReadAsync(store.Object, CancellationToken.None);

        Assert.IsTrue(captured.Contains("config.read"),
            $"Expected 'config.read' span on net481. Got: [{string.Join(", ", captured)}]");
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task PackageConfigStore_WriteAsync_EmitsConfigWriteSpan_Net481()
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

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var factory = new Mock<IPackageStoreFactory>(MockBehavior.Loose);
        factory.Setup(f => f.Create(It.IsAny<string>()))
            .Returns((store.Object, new Mock<IStateStore>().Object));

        var metrics = new Mock<IMigrationMetrics>(MockBehavior.Loose);
        var sut = new DevOpsMigrationPlatform.Infrastructure.Agent.Storage.PackageConfigStore(
            factory.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DevOpsMigrationPlatform.Infrastructure.Agent.Storage.PackageConfigStore>.Instance,
            metrics.Object);

        var configFile = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(configFile, """{"MigrationPlatform":{"Mode":"Export"}}""");
            await sut.WriteAsync("test://package", configFile, false, CancellationToken.None);
        }
        finally { System.IO.File.Delete(configFile); }

        Assert.IsTrue(captured.Contains("config.write"),
            $"Expected 'config.write' span on net481. Got: [{string.Join(", ", captured)}]");
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
            revisionSource,
            attachmentSource,
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
