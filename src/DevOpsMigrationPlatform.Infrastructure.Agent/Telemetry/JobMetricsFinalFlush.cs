// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Pushes a job's final metrics before its terminal state. Metrics reach the Control Plane on a
/// periodic export cycle, so a job that ended between two cycles reported completion with stale
/// counts and the CLI summary read "0 work items imported" (#171). This flush collects the
/// metrics now (<paramref name="collect"/>: the OTel meter provider's force-flush), then enqueues
/// the latest snapshot and flushes the worker-event channel, so the counts land first.
/// </summary>
public sealed class JobMetricsFinalFlush : IFlushable
{
    private readonly IJobMetricsStore _store;
    private readonly UnifiedWorkerEventWriter _writer;
    private readonly Action? _collect;

    public JobMetricsFinalFlush(IJobMetricsStore store, UnifiedWorkerEventWriter writer, Action? collect)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _collect = collect;
    }

    /// <inheritdoc/>
    public async Task FlushAsync()
    {
        _collect?.Invoke();
        if (_store.Latest is { } metrics)
            _writer.EnqueueMetrics(metrics);
        await _writer.FlushAsync().ConfigureAwait(false);
    }
}
