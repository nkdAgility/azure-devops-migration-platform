// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
namespace DevOpsMigrationPlatform.Abstractions.Telemetry;

/// <summary>
/// In-process store for the most recent <see cref="JobMetrics"/>.
/// Updated by <c>SnapshotMetricExporter</c> on every OTel export cycle.
/// Read by <c>ControlPlaneTelemetryTimer</c> to push to the Control Plane.
/// </summary>
public interface IJobMetricsStore
{
    /// <summary>Replaces the stored metrics with the given value.</summary>
    void Update(JobMetrics metrics);

    /// <summary>Returns the most recent metrics, or <c>null</c> if none has been recorded yet.</summary>
    JobMetrics? Latest { get; }
}
