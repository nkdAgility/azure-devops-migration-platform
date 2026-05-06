// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Instrument names for job lifecycle metrics emitted by the Control Plane.
/// All instruments live under the <see cref="WellKnownMeterNames.ControlPlane"/> meter.
/// </summary>
public static class WellKnownControlPlaneMetricNames
{
    /// <summary>Total jobs ever submitted (monotonic counter).</summary>
    public const string JobsTotal = "platform.job.queue.total";

    /// <summary>Jobs currently waiting in the queue (UpDownCounter).</summary>
    public const string JobsQueued = "platform.job.queue.depth";

    /// <summary>Jobs currently being executed by an agent (UpDownCounter).</summary>
    public const string JobsInProgress = "platform.job.execute.in_progress";

    /// <summary>Total jobs that completed successfully (monotonic counter).</summary>
    public const string JobsCompleted = "platform.job.execute.completed";

    /// <summary>Total jobs that failed (monotonic counter).</summary>
    public const string JobsFailed = "platform.job.execute.failed";

    /// <summary>Job execution duration in milliseconds (histogram), tagged with job.type.</summary>
    public const string JobDuration = "platform.job.execute.duration_ms";
}
