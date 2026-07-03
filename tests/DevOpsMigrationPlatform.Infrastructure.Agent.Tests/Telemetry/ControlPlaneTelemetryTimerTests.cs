// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class ControlPlaneTelemetryTimerTests
{
    private Mock<IJobMetricsStore> _metricsStore = null!;
    private Mock<IJobSnapshotStore> _snapshotStore = null!;
    private ActiveLeaseState _leaseState = null!;
    private MockHttpMessageHandler _handler = null!;
    private UnifiedWorkerEventWriter _writer = null!;
    private IOptions<TelemetryOptions> _options = null!;
    private ManualResetEventSlim _signal = null!;

    [TestInitialize]
    public void Setup()
    {
        _metricsStore = new Mock<IJobMetricsStore>();
        _snapshotStore = new Mock<IJobSnapshotStore>();
        _leaseState = new ActiveLeaseState { CurrentLeaseId = "lease-abc-123" };
        _handler = new MockHttpMessageHandler();
        _handler.RespondWith(HttpStatusCode.NoContent);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:5100") });

        _writer = new UnifiedWorkerEventWriter(
            httpFactory.Object,
            _leaseState,
            NullLogger<UnifiedWorkerEventWriter>.Instance);

        _options = Options.Create(new TelemetryOptions { SnapshotIntervalSeconds = 60 });
        _signal = new ManualResetEventSlim(false);
        _snapshotStore.Setup(s => s.UpdateSignal).Returns(_signal.WaitHandle);
    }

    private ControlPlaneTelemetryTimer CreateSut() =>
        new ControlPlaneTelemetryTimer(
            _metricsStore.Object,
            _snapshotStore.Object,
            _writer,
            _options,
            NullLogger<ControlPlaneTelemetryTimer>.Instance);

    /// <summary>
    /// Scenario: Migration Agent enqueues a MetricSnapshot on its configured interval.
    /// When the agent has metrics, it enqueues them into the unified event writer,
    /// which subsequently flushes them to the Control Plane.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PushesTelemetry_WhenMetricsAvailable()
    {
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 250, Completed = 250 }
            }
        };
        _metricsStore.Setup(s => s.Latest).Returns(metrics);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        // Give the timer one iteration to enqueue.
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        await _writer.FlushAsync();

        Assert.IsNotNull(_handler.LastRequestContent);
        var body = await _handler.LastRequestContent!.ReadAsStringAsync();
        StringAssert.Contains(body, "\"Metrics\"");
    }

    /// <summary>
    /// Scenario: Enqueue is skipped when no metrics or snapshot are available yet.
    /// When both stores return null, nothing is written to the Control Plane.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task SkipsEnqueue_WhenNoMetricsOrSnapshotAvailable()
    {
        _metricsStore.Setup(s => s.Latest).Returns((JobMetrics?)null);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        await _writer.FlushAsync();

        Assert.IsNull(_handler.LastRequestContent);
    }

    /// <summary>
    /// Scenario: A non-success response from the Control Plane does not crash the agent.
    /// The unified writer's own retry/backoff handles failures — the timer never awaits
    /// the HTTP call directly, so a Control Plane failure cannot propagate into the timer.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ContinuesRunning_WhenControlPlaneReturnsFailure()
    {
        _handler.RespondWith(HttpStatusCode.InternalServerError);

        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 1 }
            }
        };
        _metricsStore.Setup(s => s.Latest).Returns(metrics);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();

        // Must not throw
        await task;
    }

    /// <summary>
    /// Scenario: Enqueue is triggered when a snapshot arrives (snapshot boundary signal).
    /// The snapshot is enqueued into the unified writer for flushing to the Control Plane.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PushesSnapshot_WhenSnapshotStoreIsPopulated()
    {
        var snapshot = new JobSnapshot
        {
            Organisations = []
        };
        _metricsStore.Setup(s => s.Latest).Returns((JobMetrics?)null);
        _snapshotStore.Setup(s => s.Latest).Returns(snapshot);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        await _writer.FlushAsync();

        Assert.IsNotNull(_handler.LastRequestContent);
        var body = await _handler.LastRequestContent!.ReadAsStringAsync();
        StringAssert.Contains(body, "\"Snapshot\"");
    }

    /// <summary>
    /// Scenario: Timer completes gracefully when cancellation is requested.
    /// Ensures ExecuteAsync exits without hanging or throwing.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task StopsGracefully_WhenCancelled()
    {
        _metricsStore.Setup(s => s.Latest).Returns((JobMetrics?)null);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await cts.CancelAsync();

        // Should complete without throwing
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
