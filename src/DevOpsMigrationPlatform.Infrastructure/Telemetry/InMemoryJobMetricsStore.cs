// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Lock-free, single-value metrics store backed by a volatile reference.
/// Thread safety: volatile write/read is sufficient for the single-writer (SnapshotMetricExporter)
/// single-reader (ControlPlaneTelemetryTimer) pattern used here.
/// </summary>
public sealed class InMemoryJobMetricsStore : IJobMetricsStore
{
    private volatile JobMetrics? _latest;

    public void Update(JobMetrics metrics) =>
        _latest = metrics;

    public JobMetrics? Latest => _latest;
}
