// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Capture;

[TestClass]
public sealed class DependencyCaptureTests
{
    private const string JobId = "test-job-001";
    private const string OrgUrl = "https://dev.azure.com/testorg";
    private const string Project = "TestProject";

    // ── Helper: create a minimal InventoryContext ──────────────────────────
    private static InventoryContext CreateContext(
        IProgressSink? progressSink = null,
        string project = Project,
        IPackageAccess? package = null)
    {
        return new()
        {
            Job = new Job { JobId = JobId },
            Package = package ?? PackageTestFactory.CreateLooseMock().Object,
            ProgressSink = progressSink,
            SourceEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = OrgUrl,
                Type = "Simulated",
                Authentication = new OrganisationEndpointAuthentication { Type = AuthenticationType.None }
            },
            Project = project,
            Organisations = Array.AsReadOnly(new[]
            {
                new ScopedOrganisationEndpoint
                {
                    Endpoint = new SimulatedEndpointOptions { Url = OrgUrl },
                    Projects = new List<string> { project }
                }
            }),
            Policies = new JobPolicies()
        };
    }

    // ── Helper: build a capture instance with mocked deps ─────────────────
    private static DependencyCapture CreateCapture(
        Mock<IDependencyDiscoveryServiceFactory> factory,
        Mock<IDependencyOrchestrator> orchestrator,
        IPlatformMetrics? metrics = null,
        IProgressSink? progressSink = null,
        ILogger<DependencyCapture>? logger = null,
        double slowCaptureThresholdMs = 30_000)
        => new(
            factory.Object,
            orchestrator.Object,
            logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyCapture>(),
            metrics,
            progressSink,
            slowCaptureThresholdMs);

    // ── T023 — Happy path ──────────────────────────────────────────────────
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_HappyPath_CallsCreateForProjectAndCaptureProjectAsync()
    {
        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters());

        var capture = CreateCapture(factory, orchestrator);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        factory.VerifyAll();
        orchestrator.VerifyAll();
    }

    // ── T023 — Exception propagates ────────────────────────────────────────
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_WhenOrchestratorThrows_PropagatesException()
    {
        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test failure"));

        var capture = CreateCapture(factory, orchestrator);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => capture.CaptureAsync(CreateContext(), CancellationToken.None));
    }

    // ── T031 — O-1 Traces ──────────────────────────────────────────────────
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CaptureAsync_O1_OpensRootSpanAndChildSpans()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WellKnownActivitySourceNames.Discovery,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters());

        var capture = CreateCapture(factory, orchestrator);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        // Root span
        var root = activities.FirstOrDefault(a => a.OperationName == "dependency.capture");
        Assert.IsNotNull(root, "Root span 'dependency.capture' not found");
        Assert.AreEqual(JobId, root.Tags.FirstOrDefault(t => t.Key == "job.id").Value);
        Assert.AreEqual(OrgUrl, root.Tags.FirstOrDefault(t => t.Key == "org.url").Value);
        Assert.AreEqual(Project, root.Tags.FirstOrDefault(t => t.Key == "project.name").Value);
        Assert.AreEqual("dependencies", root.Tags.FirstOrDefault(t => t.Key == "capture.handler").Value);

        // Child spans
        Assert.IsTrue(activities.Any(a => a.OperationName == "dependency.capture.create_service"),
            "Child span 'dependency.capture.create_service' not found");
        Assert.IsTrue(activities.Any(a => a.OperationName == "dependency.capture.execute"),
            "Child span 'dependency.capture.execute' not found");
        Assert.IsTrue(activities.Any(a => a.OperationName == "dependency.capture.write_csv"),
            "Child span 'dependency.capture.write_csv' not found");
    }

    // ── T032 — O-2 Metrics ─────────────────────────────────────────────────
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O2_SuccessPath_RecordsAllRequiredMetrics()
    {
        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);

        // Strict mocking: ALL expected calls must be set up
        metrics.Setup(m => m.DependenciesCaptureInFlightIncrement(
                It.Is<MetricsTagList>(t => HasDefaultDependencyMetricTags(t))))
            .Verifiable();
        metrics.Setup(m => m.DependenciesCaptureStarted(
                It.Is<MetricsTagList>(t => HasDefaultDependencyMetricTags(t))))
            .Verifiable();
        metrics.Setup(m => m.DependenciesCaptureCompleted(
                It.Is<MetricsTagList>(t => HasDefaultDependencyMetricTags(t))))
            .Verifiable();
        metrics.Setup(m => m.RecordDependenciesCaptureDuration(
                It.IsAny<double>(),
                It.Is<MetricsTagList>(t => HasDefaultDependencyMetricTags(t))))
            .Verifiable();
        metrics.Setup(m => m.RecordWorkItemsAnalysed(
                It.IsAny<int>(),
                It.Is<MetricsTagList>(t => HasDefaultDependencyMetricTags(t))))
            .Verifiable();
        metrics.Setup(m => m.DependenciesCaptureInFlightDecrement(
                It.Is<MetricsTagList>(t => HasDefaultDependencyMetricTags(t))))
            .Verifiable();

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters());

        var capture = CreateCapture(factory, orchestrator, metrics.Object);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        metrics.VerifyAll();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O2_FailurePath_RecordsFailedAndDurationAndDecrementsInFlight()
    {
        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);

        metrics.Setup(m => m.DependenciesCaptureInFlightIncrement(It.IsAny<MetricsTagList>())).Verifiable();
        metrics.Setup(m => m.DependenciesCaptureStarted(It.IsAny<MetricsTagList>())).Verifiable();
        metrics.Setup(m => m.DependenciesCaptureFailed(It.IsAny<MetricsTagList>())).Verifiable();
        metrics.Setup(m => m.RecordDependenciesCaptureDuration(It.IsAny<double>(), It.IsAny<MetricsTagList>())).Verifiable();
        metrics.Setup(m => m.DependenciesCaptureInFlightDecrement(It.IsAny<MetricsTagList>())).Verifiable();

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sim failure"));

        var capture = CreateCapture(factory, orchestrator, metrics.Object);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => capture.CaptureAsync(CreateContext(), CancellationToken.None));

        metrics.Verify(m => m.DependenciesCaptureFailed(It.IsAny<MetricsTagList>()), Times.Once);
        metrics.Verify(m => m.RecordDependenciesCaptureDuration(It.IsAny<double>(), It.IsAny<MetricsTagList>()), Times.Once);
        metrics.Verify(m => m.DependenciesCaptureInFlightDecrement(It.IsAny<MetricsTagList>()), Times.Once);
        // DependenciesCaptureCompleted must NOT be called
        metrics.Verify(m => m.DependenciesCaptureCompleted(It.IsAny<MetricsTagList>()), Times.Never);
    }

    // ── T033 — O-4 ProgressSink ────────────────────────────────────────────
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O4_SuccessPath_EmitsCapturingAndCapturedEvents()
    {
        var emittedEvents = new List<ProgressEvent>();
        var sink = new Mock<IProgressSink>(MockBehavior.Strict);
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters
            {
                WorkItemsAnalysed = 42,
                ExternalLinksFound = 7,
                CrossProjectLinks = 3,
                CrossOrgLinks = 1
            });

        var capture = CreateCapture(factory, orchestrator, progressSink: sink.Object);
        await capture.CaptureAsync(CreateContext(sink.Object), CancellationToken.None);

        Assert.IsTrue(emittedEvents.Any(e => e.Stage == "Capturing"), "Missing 'Capturing' event");
        var capturedEvent = emittedEvents.FirstOrDefault(e => e.Stage == "Captured");
        Assert.IsNotNull(capturedEvent, "Missing 'Captured' event");
        var deps = capturedEvent!.Metrics?.Discovery?.Dependencies;
        Assert.IsNotNull(deps, "Captured event must carry non-null Metrics.Discovery.Dependencies");
        Assert.AreEqual(42, deps.WorkItemsAnalysed, "WorkItemsAnalysed must match orchestrator return value");
        Assert.AreEqual(7, deps.ExternalLinksFound, "ExternalLinksFound must match orchestrator return value");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O4_FailurePath_EmitsFailedEvent()
    {
        var emittedEvents = new List<ProgressEvent>();
        var sink = new Mock<IProgressSink>(MockBehavior.Strict);
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sim failure"));

        var capture = CreateCapture(factory, orchestrator, progressSink: sink.Object);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => capture.CaptureAsync(CreateContext(sink.Object), CancellationToken.None));

        Assert.IsTrue(emittedEvents.Any(e => e.Stage == "Failed"), "Missing 'Failed' event");
    }

    // ── T044 — O-3 Log assertions ──────────────────────────────────────────
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O3_SuccessPath_LogsStartAndCompletionWithStructuredParams()
    {
        var logger = new Mock<ILogger<DependencyCapture>>();

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters());

        var capture = new DependencyCapture(factory.Object, orchestrator.Object, logger.Object);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        // Start log: structured params must include Org=OrgUrl and Project=Project
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => LogStateHasOrgAndProject(v)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce(),
            "Start log must carry Org and Project as structured parameters");

        // Completion log: must include DurationMs (>= 0 double)
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => LogStateHasDurationMs(v)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Completion log must carry DurationMs as a structured parameter");

        // Total: exactly 2 Information calls (start + completion)
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2),
            "Expected exactly 2 LogInformation calls: start and completion");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O3_FailurePath_LogsErrorWithStructuredParams()
    {
        var logger = new Mock<ILogger<DependencyCapture>>();

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sim error"));

        var capture = new DependencyCapture(factory.Object, orchestrator.Object, logger.Object);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => capture.CaptureAsync(CreateContext(), CancellationToken.None));

        // Error log must be called exactly once with Org, Project, ErrorType, ErrorMessage as named params
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => LogStateHasErrorParams(v)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "LogError must be called exactly once with Org, Project, ErrorType, and ErrorMessage structured params");
    }

    private static bool LogStateHasOrgAndProject(object v)
    {
        var state = v as IReadOnlyList<KeyValuePair<string, object?>>;
        return state != null
            && state.Any(kv => kv.Key == "Org" && kv.Value?.ToString() == OrgUrl)
            && state.Any(kv => kv.Key == "Project" && kv.Value?.ToString() == Project)
            && state.Any(kv => kv.Key == "Handler" && kv.Value is string h && h.Length > 0)
            && state.Any(kv => kv.Key == "JobId" && kv.Value?.ToString() == JobId);
    }

    private static bool LogStateHasDurationMs(object v)
    {
        var state = v as IReadOnlyList<KeyValuePair<string, object?>>;
        return state != null
            && state.Any(kv => kv.Key == "DurationMs" && kv.Value is double d && d >= 0)
            && state.Any(kv => kv.Key == "OutputPath" && kv.Value is string p && p.Length > 0)
            && state.Any(kv => kv.Key == "JobId" && kv.Value?.ToString() == JobId);
    }

    private static bool LogStateHasErrorParams(object v)
    {
        var state = v as IReadOnlyList<KeyValuePair<string, object?>>;
        return state != null
            && state.Any(kv => kv.Key == "Org" && kv.Value?.ToString() == OrgUrl)
            && state.Any(kv => kv.Key == "Project" && kv.Value?.ToString() == Project)
            && state.Any(kv => kv.Key == "ErrorType" && kv.Value?.ToString() == nameof(InvalidOperationException))
            && state.Any(kv => kv.Key == "ErrorMessage" && kv.Value?.ToString() == "sim error")
            && state.Any(kv => kv.Key == "DurationMs")
            && state.Any(kv => kv.Key == "JobId" && kv.Value?.ToString() == JobId);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O3_WhenCsvAlreadyExists_LogsDebugWithOutputPath()
    {
        var logger = new Mock<ILogger<DependencyCapture>>();

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);
        var packageMock = PackageTestFactory.CreateLooseMock();

        packageMock.Setup(p => p.RequestIndexAsync(
                It.Is<PackageIndexContext>(c =>
                    c.FileName == "dependencies.csv"
                    && c.Organisation == "testorg"
                    && c.Project == "TestProject"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("header\n")), "text/csv")); // file already exists → should trigger debug log

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters());

        var capture = new DependencyCapture(factory.Object, orchestrator.Object, logger.Object);
        await capture.CaptureAsync(CreateContext(package: packageMock.Object), CancellationToken.None);

        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => LogStateHasOutputPath(v)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "LogDebug must be called exactly once with OutputPath when CSV already exists");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_O3_WhenCaptureDurationExceedsThreshold_LogsWarning()
    {
        var logger = new Mock<ILogger<DependencyCapture>>();

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters());

        // Pass threshold = 0 so any real capture duration is "slow"
        var capture = CreateCapture(factory, orchestrator, logger: logger.Object, slowCaptureThresholdMs: 0);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => LogStateHasSlowWarningParams(v)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "LogWarning must be called exactly once with Dependency, DurationMs, ThresholdMs when slow");
    }

    // ── Cross-organisation link detection ─────────────────────────────────
    // Scenario: Detect cross-organisation links
    //   Given project "ProjectA" has work items linking to a different organisation
    //   When I run dependency discovery for "ProjectA"
    //   Then the dependencies report should flag cross-organisation links
    //   And cross-organisation links should be counted separately
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_WhenProjectHasCrossOrgLinks_CapturedEventCountsCrossOrgLinksSeparately()
    {
        // Arrange: orchestrator returns counters with both cross-project and cross-org links
        var emittedEvents = new List<ProgressEvent>();
        var sink = new Mock<IProgressSink>(MockBehavior.Strict);
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        var service = new Mock<IDependencyDiscoveryService>(MockBehavior.Strict);

        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                OrgUrl, Project,
                It.IsAny<JobPolicies>()))
            .Returns(service.Object);

        orchestrator.Setup(o => o.CaptureProjectAsync(
                service.Object,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependencyCounters
            {
                WorkItemsAnalysed = 10,
                ExternalLinksFound = 5,
                CrossProjectLinks = 3,
                CrossOrgLinks = 2   // cross-org links to a different organisation
            });

        var capture = CreateCapture(factory, orchestrator, progressSink: sink.Object);
        await capture.CaptureAsync(CreateContext(sink.Object), CancellationToken.None);

        // Assert: the Captured event carries cross-org links separately from cross-project links
        var capturedEvent = emittedEvents.FirstOrDefault(e => e.Stage == "Captured");
        Assert.IsNotNull(capturedEvent, "A 'Captured' progress event must be emitted after dependency capture.");
        var deps = capturedEvent!.Metrics?.Discovery?.Dependencies;
        Assert.IsNotNull(deps, "Captured event must include Metrics.Discovery.Dependencies.");
        Assert.AreEqual(2, deps.CrossOrgLinks,
            "Cross-organisation links must be counted separately and reported as CrossOrgLinks.");
        Assert.AreEqual(3, deps.CrossProjectLinks,
            "Cross-project links must remain separate from cross-organisation links.");
        Assert.AreEqual(5, deps.ExternalLinksFound,
            "Total ExternalLinksFound must include both cross-project and cross-org links.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static bool LogStateHasOutputPath(object v)
    {
        var state = v as IReadOnlyList<KeyValuePair<string, object?>>;
        return state != null
            && state.Any(kv => kv.Key == "OutputPath" && kv.Value is string p && p.Length > 0);
    }

    private static bool LogStateHasSlowWarningParams(object v)
    {
        var state = v as IReadOnlyList<KeyValuePair<string, object?>>;
        return state != null
            && state.Any(kv => kv.Key == "Dependency" && kv.Value is string d && d.Length > 0)
            && state.Any(kv => kv.Key == "DurationMs" && kv.Value is double dur && dur >= 0)
            && state.Any(kv => kv.Key == "ThresholdMs");
    }

    private static bool HasTag(MetricsTagList tags, string key, string value)
    {
        for (var i = 0; i < tags.Count; i++)
        {
            if (tags[i].Key == key && tags[i].Value?.ToString() == value)
                return true;
        }
        return false;
    }

    private static bool HasDefaultDependencyMetricTags(MetricsTagList tags)
        => HasTag(tags, "job.id", JobId)
            && HasTag(tags, "operation", "capture")
            && HasTag(tags, "module", "dependencies");
}
