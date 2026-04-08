using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Background service that pushes the latest <see cref="MetricSnapshot"/> to the
/// Control Plane on a configurable interval while a lease is held.
/// Reads the current lease id from <see cref="ActiveLeaseState"/> — no push occurs
/// when <see cref="ActiveLeaseState.CurrentLeaseId"/> is null.
/// </summary>
internal sealed class ControlPlaneTelemetryTimer : BackgroundService
{
    private readonly IMetricSnapshotStore _store;
    private readonly IControlPlaneTelemetryClient _client;
    private readonly ActiveLeaseState _leaseState;
    private readonly IOptions<TelemetryOptions> _options;
    private readonly ILogger<ControlPlaneTelemetryTimer> _logger;

    public ControlPlaneTelemetryTimer(
        IMetricSnapshotStore store,
        IControlPlaneTelemetryClient client,
        ActiveLeaseState leaseState,
        IOptions<TelemetryOptions> options,
        ILogger<ControlPlaneTelemetryTimer> logger)
    {
        _store      = store;
        _client     = client;
        _leaseState = leaseState;
        _options    = options;
        _logger     = logger;
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

            var leaseId  = _leaseState.CurrentLeaseId;
            var snapshot = _store.Latest;

            if (leaseId is null || snapshot is null)
                continue;

            await _client.PushSnapshotAsync(leaseId, snapshot, stoppingToken)
                         .ConfigureAwait(false);
        }

        _logger.LogDebug("ControlPlaneTelemetryTimer stopped.");
    }
}
