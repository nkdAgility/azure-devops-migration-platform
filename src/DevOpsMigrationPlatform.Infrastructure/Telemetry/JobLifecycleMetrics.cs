using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Concrete implementation of <see cref="Abstractions.IJobLifecycleMetrics"/> backed by
/// the <see cref="Abstractions.WellKnownMeterNames.ControlPlane"/> meter.
/// </summary>
public sealed class JobLifecycleMetrics : Abstractions.IJobLifecycleMetrics
{
    private readonly Counter<long> _total;
    private readonly UpDownCounter<int> _queued;
    private readonly UpDownCounter<int> _inProgress;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _duration;

    public JobLifecycleMetrics()
    {
        var meter = new Meter(
            Abstractions.WellKnownMeterNames.ControlPlane,
            "1.0");

        _total = meter.CreateCounter<long>(
            Abstractions.WellKnownJobMetricNames.JobsTotal,
            unit: "{job}",
            description: "Total jobs ever submitted.");

        _queued = meter.CreateUpDownCounter<int>(
            Abstractions.WellKnownJobMetricNames.JobsQueued,
            unit: "{job}",
            description: "Jobs currently waiting in the queue.");

        _inProgress = meter.CreateUpDownCounter<int>(
            Abstractions.WellKnownJobMetricNames.JobsInProgress,
            unit: "{job}",
            description: "Jobs currently being executed by an agent.");

        _completed = meter.CreateCounter<long>(
            Abstractions.WellKnownJobMetricNames.JobsCompleted,
            unit: "{job}",
            description: "Total jobs completed successfully.");

        _failed = meter.CreateCounter<long>(
            Abstractions.WellKnownJobMetricNames.JobsFailed,
            unit: "{job}",
            description: "Total jobs that failed.");

        _duration = meter.CreateHistogram<double>(
            Abstractions.WellKnownJobMetricNames.JobDuration,
            unit: "ms",
            description: "Job execution duration in milliseconds.");
    }

    /// <inheritdoc />
    public void JobSubmitted(in TagList tags)
    {
        _total.Add(1, tags);
        _queued.Add(1, tags);
    }

    /// <inheritdoc />
    public void JobDequeued(in TagList tags)
    {
        _queued.Add(-1, tags);
    }

    /// <inheritdoc />
    public void JobStarted(in TagList tags)
    {
        _inProgress.Add(1, tags);
    }

    /// <inheritdoc />
    public void JobCompleted(in TagList tags)
    {
        _inProgress.Add(-1, tags);
        _completed.Add(1, tags);
    }

    /// <inheritdoc />
    public void JobFailed(in TagList tags)
    {
        _inProgress.Add(-1, tags);
        _failed.Add(1, tags);
    }

    /// <inheritdoc />
    public void RecordJobDuration(double milliseconds, in TagList tags)
    {
        _duration.Record(milliseconds, tags);
    }
}
