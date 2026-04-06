using System;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

public class AttachmentDownloadMetrics : IAttachmentDownloadMetrics
{
    public const string MeterName = WellKnownMeterNames.AttachmentDownload;
    public const string MeterVersion = "1.0";

    private static readonly Meter _meter = new Meter(MeterName, MeterVersion);

    private static readonly Counter<long> _attempts =
        _meter.CreateCounter<long>(WellKnownMetricNames.AttachmentAttempts);

    private static readonly Counter<long> _successes =
        _meter.CreateCounter<long>(WellKnownMetricNames.AttachmentSuccesses);

    private static readonly Counter<long> _failures =
        _meter.CreateCounter<long>(WellKnownMetricNames.AttachmentFailures);

    private static readonly Histogram<double> _duration =
        _meter.CreateHistogram<double>(WellKnownMetricNames.AttachmentDuration, unit: "ms");

    public void RecordAttempt() => _attempts.Add(1);
    public void RecordSuccess() => _successes.Add(1);
    public void RecordFailure() => _failures.Add(1);
    public void RecordDuration(TimeSpan duration) => _duration.Record(duration.TotalMilliseconds);
}
