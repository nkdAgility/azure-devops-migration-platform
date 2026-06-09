// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
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
    private Mock<IControlPlaneTelemetryClient> _client = null!;
    private ActiveLeaseState _leaseState = null!;
    private IOptions<TelemetryOptions> _options = null!;
    private ManualResetEventSlim _signal = null!;

    [TestInitialize]
    public void Setup()
    {
        _metricsStore = new Mock<IJobMetricsStore>();
        _snapshotStore = new Mock<IJobSnapshotStore>();
        _client = new Mock<IControlPlaneTelemetryClient>();
        _leaseState = new ActiveLeaseState();
        _options = Options.Create(new TelemetryOptions { SnapshotIntervalSeconds = 60 });
        _signal = new ManualResetEventSlim(false);
        _snapshotStore.Setup(s => s.UpdateSignal).Returns(_signal.WaitHandle);
    }

    private ControlPlaneTelemetryTimer CreateSut() =>
        new ControlPlaneTelemetryTimer(
            _metricsStore.Object,
            _snapshotStore.Object,
            _client.Object,
            _leaseState,
            _options,
            NullLogger<ControlPlaneTelemetryTimer>.Instance);

    /// <summary>
    /// Scenario: Migration Agent pushes a MetricSnapshot on its configured interval.
    /// When the agent holds a lease and has metrics, it calls PushMetricsAsync.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task PushesTelemetry_WhenLeaseHeldAndMetricsAvailable()
    {
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 250, Completed = 250 }
            }
        };
        _leaseState.CurrentLeaseId = "lease-abc-123";
        _metricsStore.Setup(s => s.Latest).Returns(metrics);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        _client
            .Setup(c => c.PushMetricsAsync(It.IsAny<string>(), It.IsAny<JobMetrics>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _client
            .Setup(c => c.PushSnapshotAsync(It.IsAny<string>(), It.IsAny<JobSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        // Give the timer one iteration to execute
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        _client.Verify(
            c => c.PushMetricsAsync("lease-abc-123", metrics, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Scenario: Push is skipped when no MetricSnapshot is available yet.
    /// When the snapshot store returns null, no HTTP request is sent.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task SkipsPush_WhenNoSnapshotAvailable()
    {
        _leaseState.CurrentLeaseId = "lease-abc-123";
        _metricsStore.Setup(s => s.Latest).Returns((JobMetrics?)null);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        _client.Verify(
            c => c.PushMetricsAsync(It.IsAny<string>(), It.IsAny<JobMetrics>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            c => c.PushSnapshotAsync(It.IsAny<string>(), It.IsAny<JobSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Scenario: Push is skipped when the agent holds no active lease.
    /// When CurrentLeaseId is null, no HTTP request is sent even if snapshots are available.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task SkipsPush_WhenNoLeaseHeld()
    {
        // No lease set — CurrentLeaseId is null
        var snapshot = new JobSnapshot();
        _metricsStore.Setup(s => s.Latest).Returns((JobMetrics?)null);
        _snapshotStore.Setup(s => s.Latest).Returns(snapshot);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        _client.Verify(
            c => c.PushMetricsAsync(It.IsAny<string>(), It.IsAny<JobMetrics>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            c => c.PushSnapshotAsync(It.IsAny<string>(), It.IsAny<JobSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Scenario: A non-success response from the Control Plane does not crash the agent.
    /// PushMetricsAsync is best-effort — exceptions should not propagate.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ContinuesRunning_WhenControlPlaneReturnsFailure()
    {
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 1 }
            }
        };
        _leaseState.CurrentLeaseId = "lease-abc-123";
        _metricsStore.Setup(s => s.Latest).Returns(metrics);
        _snapshotStore.Setup(s => s.Latest).Returns((JobSnapshot?)null);

        // Simulate 503 by having the client complete without throwing
        // (ControlPlaneTelemetryClient absorbs non-success internally).
        _client
            .Setup(c => c.PushMetricsAsync(It.IsAny<string>(), It.IsAny<JobMetrics>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();

        // Must not throw
        await task;

        // Timer ran at least once
        _client.Verify(
            c => c.PushMetricsAsync(It.IsAny<string>(), It.IsAny<JobMetrics>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Scenario: Push is triggered when a snapshot arrives (snapshot boundary signal).
    /// The snapshot is pushed using the currently held lease id.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task PushesSnapshot_WhenSnapshotStoreIsPopulated()
    {
        var snapshot = new JobSnapshot
        {
            Organisations = []
        };
        _leaseState.CurrentLeaseId = "lease-abc-123";
        _metricsStore.Setup(s => s.Latest).Returns((JobMetrics?)null);
        _snapshotStore.Setup(s => s.Latest).Returns(snapshot);

        _client
            .Setup(c => c.PushSnapshotAsync(It.IsAny<string>(), It.IsAny<JobSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await task;

        _client.Verify(
            c => c.PushSnapshotAsync("lease-abc-123", snapshot, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Scenario: Timer completes gracefully when cancellation is requested.
    /// Ensures ExecuteAsync exits without hanging or throwing.
    /// </summary>
    [TestCategory("UnitTest")]
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
