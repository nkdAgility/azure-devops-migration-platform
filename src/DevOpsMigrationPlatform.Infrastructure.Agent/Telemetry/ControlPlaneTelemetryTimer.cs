// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Background service that enqueues the latest <see cref="JobMetrics"/> and
/// <see cref="JobSnapshot"/> into <see cref="UnifiedWorkerEventWriter"/> on a
/// configurable interval while a lease is held.
/// </summary>
public sealed class ControlPlaneTelemetryTimer : BackgroundService
{
    private readonly IJobMetricsStore _metricsStore;
    private readonly IJobSnapshotStore _snapshotStore;
    private readonly UnifiedWorkerEventWriter _writer;
    private readonly IOptions<TelemetryOptions> _options;
    private readonly ILogger<ControlPlaneTelemetryTimer> _logger;

    public ControlPlaneTelemetryTimer(
        IJobMetricsStore metricsStore,
        IJobSnapshotStore snapshotStore,
        UnifiedWorkerEventWriter writer,
        IOptions<TelemetryOptions> options,
        ILogger<ControlPlaneTelemetryTimer> logger)
    {
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _writer = writer;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("ControlPlaneTelemetryTimer started.");

        var snapshotSignal = _snapshotStore.UpdateSignal;

        while (!stoppingToken.IsCancellationRequested)
        {
            var metrics = _metricsStore.Latest;
            if (metrics is not null)
                _writer.EnqueueMetrics(metrics);

            var snapshot = _snapshotStore.Latest;
            if (snapshot is not null)
                _writer.EnqueueSnapshot(snapshot);

            var intervalSeconds = _options.Value.SnapshotIntervalSeconds;
            try
            {
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
                    // Woken by snapshot signal — push immediately.
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
