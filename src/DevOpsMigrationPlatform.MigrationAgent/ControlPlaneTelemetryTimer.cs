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

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = _options.Value.SnapshotIntervalSeconds;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken)
                          .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var leaseId = _leaseState.CurrentLeaseId;
            if (leaseId is null)
                continue;

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

        _logger.LogDebug("ControlPlaneTelemetryTimer stopped.");
    }
}
