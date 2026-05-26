// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

/// <summary>
/// Attachment download metrics emitted under the consolidated <see cref="WellKnownMeterNames.Agent"/> meter.
/// Used by the net481 TFS subprocess where <c>IPlatformMetrics</c> (which requires <c>TagList</c>) is unavailable.
/// </summary>
public class AttachmentDownloadMetrics : IAttachmentDownloadMetrics
{
    // Inline metric names because WellKnownMetricNames constants are not used in this net481 path.
    private const string AttachmentAttemptsName = "attachment_download_attempt_total";
    private const string AttachmentSuccessesName = "attachment_download_success_total";
    private const string AttachmentFailuresName = "attachment_download_failure_total";
    private const string AttachmentDurationName = "attachment_download_duration_ms";

    public const string MeterName = WellKnownMeterNames.Agent;
    public const string MeterVersion = "1.0";

    private static readonly Meter _meter = new Meter(MeterName, MeterVersion);

    private static readonly Counter<long> _attempts =
        _meter.CreateCounter<long>(AttachmentAttemptsName);

    private static readonly Counter<long> _successes =
        _meter.CreateCounter<long>(AttachmentSuccessesName);

    private static readonly Counter<long> _failures =
        _meter.CreateCounter<long>(AttachmentFailuresName);

    private static readonly Histogram<double> _duration =
        _meter.CreateHistogram<double>(AttachmentDurationName, unit: "ms");

    public void RecordAttempt() => _attempts.Add(1);
    public void RecordSuccess() => _successes.Add(1);
    public void RecordFailure() => _failures.Add(1);
    public void RecordDuration(TimeSpan duration) => _duration.Record(duration.TotalMilliseconds);
}
