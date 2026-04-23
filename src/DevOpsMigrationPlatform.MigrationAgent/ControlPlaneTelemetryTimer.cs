using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Background service that pushes the latest <see cref="JobMetrics"/> and
/// <see cref="JobSnapshot"/> to the Control Plane on a configurable interval
/// while a lease is held.
/// Reads the current lease id from <see cref="ActiveLeaseState"/> — no push occurs
/// when <see cref="ActiveLeaseState.CurrentLeaseId"/> is null.
/// </summary>
internal sealed class ControlPlaneTelemetryTimer : BackgroundService
{
    private readonly IJobMetricsStore _metricsStore;
    private readonly IJobSnapshotStore _snapshotStore;
    private readonly IControlPlaneTelemetryClient _client;
    private readonly ActiveLeaseState _leaseState;
    private readonly IOptions<TelemetryOptions> _options;
    private readonly ILogger<ControlPlaneTelemetryTimer> _logger;

    public ControlPlaneTelemetryTimer(
        IJobMetricsStore metricsStore,
        IJobSnapshotStore snapshotStore,
        IControlPlaneTelemetryClient client,
        ActiveLeaseState leaseState,
        IOptions<TelemetryOptions> options,
        ILogger<ControlPlaneTelemetryTimer> logger)
    {
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _client = client;
        _leaseState = leaseState;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("ControlPlaneTelemetryTimer started.");

        // RegisteredWaitHandle for snapshot boundary push signal.
        // When the snapshot store signals, we cancel the current delay to push immediately.
        var snapshotSignal = _snapshotStore.UpdateSignal;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Push immediately on first iteration (and after snapshot signal),
            // then delay between subsequent pushes.
            var leaseId = _leaseState.CurrentLeaseId;
            if (leaseId is not null)
            {
                var metrics = _metricsStore.Latest;
                if (metrics is not null)
                {
                    await _client.PushMetricsAsync(leaseId, metrics, stoppingToken)
                                 .ConfigureAwait(false);
                }

                var snapshot = _snapshotStore.Latest;
                if (snapshot is not null)
                {
                    await _client.PushSnapshotAsync(leaseId, snapshot, stoppingToken)
                                 .ConfigureAwait(false);
                }
            }

            var intervalSeconds = _options.Value.SnapshotIntervalSeconds;
            try
            {
                // Wait for either the timer interval or a snapshot boundary signal.
                var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var registration = ThreadPool.RegisterWaitForSingleObject(
                    snapshotSignal,
                    (_, _) => delayCts.Cancel(),
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: true);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), delayCts.Token)
                              .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Woken by snapshot signal — proceed to push immediately.
                }
                finally
                {
                    registration.Unregister(null);
                    delayCts.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogDebug("ControlPlaneTelemetryTimer stopped.");
    }
}
