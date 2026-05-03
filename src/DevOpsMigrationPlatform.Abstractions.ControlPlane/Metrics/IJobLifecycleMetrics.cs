// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlane.Metrics;

/// <summary>
/// Records job lifecycle metrics: submissions, queue depth, and in-progress count.
/// Implementations use <see cref="WellKnownMeterNames.ControlPlane"/> meter instruments.
/// </summary>
public interface IJobLifecycleMetrics
{
    /// <summary>Increments the total-jobs counter and the queued gauge.</summary>
    void JobSubmitted(in TagList tags);

    /// <summary>Decrements the queued gauge (job picked up by an agent).</summary>
    void JobDequeued(in TagList tags);

    /// <summary>Increments the in-progress gauge (agent started executing).</summary>
    void JobStarted(in TagList tags);

    /// <summary>Decrements the in-progress gauge and increments the completed counter.</summary>
    void JobCompleted(in TagList tags);

    /// <summary>Decrements the in-progress gauge and increments the failed counter.</summary>
    void JobFailed(in TagList tags);

    /// <summary>Records job execution duration in milliseconds (histogram).</summary>
    void RecordJobDuration(double milliseconds, in TagList tags);
}
