// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
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
        string project = Project)
        => new()
        {
            Job = new Job { JobId = JobId },
            ArtefactStore = new Mock<IArtefactStore>(MockBehavior.Strict).Object,
            StateStore = new Mock<IStateStore>(MockBehavior.Strict).Object,
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

    // ── Helper: build a capture instance with mocked deps ─────────────────
    private static DependencyCapture CreateCapture(
        Mock<IDependencyDiscoveryServiceFactory> factory,
        Mock<IDependencyOrchestrator> orchestrator,
        IPlatformMetrics? metrics = null,
        IProgressSink? progressSink = null)
        => new(
            factory.Object,
            orchestrator.Object,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyCapture>(),
            metrics,
            progressSink);

    // ── T023 — Happy path ──────────────────────────────────────────────────
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
            .Returns(Task.CompletedTask);

        var capture = CreateCapture(factory, orchestrator);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        factory.VerifyAll();
        orchestrator.VerifyAll();
    }

    // ── T023 — Exception propagates ────────────────────────────────────────
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
            .Returns(Task.CompletedTask);

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
    [TestMethod]
    public async Task CaptureAsync_O2_SuccessPath_RecordsAllRequiredMetrics()
    {
        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);

        // Strict mocking: ALL expected calls must be set up
        metrics.Setup(m => m.DependenciesCaptureInFlightIncrement(
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", JobId) && HasTag(t, "org.url", OrgUrl) && HasTag(t, "project.name", Project))))
            .Verifiable();
        metrics.Setup(m => m.DependenciesCaptureStarted(
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", JobId))))
            .Verifiable();
        metrics.Setup(m => m.DependenciesCaptureCompleted(
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", JobId))))
            .Verifiable();
        metrics.Setup(m => m.RecordDependenciesCaptureDuration(
                It.IsAny<double>(),
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", JobId))))
            .Verifiable();
        metrics.Setup(m => m.DependenciesCaptureInFlightDecrement(
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", JobId))))
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
            .Returns(Task.CompletedTask);

        var capture = CreateCapture(factory, orchestrator, metrics.Object);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        metrics.VerifyAll();
    }

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
            .Returns(Task.CompletedTask);

        var capture = CreateCapture(factory, orchestrator, progressSink: sink.Object);
        await capture.CaptureAsync(CreateContext(sink.Object), CancellationToken.None);

        Assert.IsTrue(emittedEvents.Any(e => e.Stage == "Capturing"), "Missing 'Capturing' event");
        var capturedEvent = emittedEvents.FirstOrDefault(e => e.Stage == "Captured");
        Assert.IsNotNull(capturedEvent, "Missing 'Captured' event");
        Assert.IsNotNull(capturedEvent!.Metrics?.Discovery?.Dependencies,
            "Captured event must carry non-null Metrics.Discovery.Dependencies");
    }

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
    [TestMethod]
    public async Task CaptureAsync_O3_SuccessPath_LogsStartAndCompletion()
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
            .Returns(Task.CompletedTask);

        var capture = new DependencyCapture(factory.Object, orchestrator.Object, logger.Object);
        await capture.CaptureAsync(CreateContext(), CancellationToken.None);

        // Verify at least two LogInformation calls (start + completion)
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(OrgUrl)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());

        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }

    [TestMethod]
    public async Task CaptureAsync_O3_FailurePath_LogsError()
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

        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static bool HasTag(MetricsTagList tags, string key, string value)
    {
        for (var i = 0; i < tags.Count; i++)
        {
            if (tags[i].Key == key && tags[i].Value?.ToString() == value)
                return true;
        }
        return false;
    }
}
