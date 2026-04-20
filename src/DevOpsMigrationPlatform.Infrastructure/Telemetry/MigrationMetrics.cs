using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Concrete implementation of <see cref="IMigrationMetrics"/> that registers all 24 instruments
/// under the <see cref="WellKnownMeterNames.Migration"/> meter.
/// Thread-safe: all OTel instrument operations are lock-free.
/// </summary>
internal sealed class MigrationMetrics : IMigrationMetrics, IDisposable
{
    private readonly Meter _meter;

    // --- Execution counters ---
    private readonly Counter<long> _attempted;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _retried;
    private readonly Histogram<double> _duration;

    // --- Payload / Complexity histograms ---
    private readonly Histogram<int> _fieldCount;
    private readonly Histogram<int> _attachmentCount;
    private readonly Histogram<int> _linkCount;
    private readonly Histogram<int> _revisionCount;
    private readonly Histogram<long> _payloadBytes;

    // --- Correctness (Tier 3) ---
    private readonly Histogram<int> _revisionSourceCount;
    private readonly Histogram<int> _revisionTargetCount;
    private readonly Histogram<int> _revisionDelta;
    private readonly Counter<long> _revisionsMissing;
    private readonly Counter<long> _revisionOrderErrors;
    private readonly Counter<long> _brokenLinks;
    private readonly Counter<long> _missingWorkItems;

    // --- In-Flight ---
    private readonly UpDownCounter<int> _inFlight;

    // --- Idempotency (deferred) ---
    private readonly Counter<long> _duplicated;
    private readonly Counter<long> _changedOnRerun;
    private readonly Counter<long> _reprocessedAfterResume;
    private readonly Counter<long> _duplicatedAfterResume;
    private readonly Counter<long> _missingAfterResume;

    public MigrationMetrics()
    {
        _meter = new Meter(WellKnownMeterNames.Migration, "2.0");

        // Execution
        _attempted = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsAttempted, unit: "{work_item}");
        _completed = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsCompleted, unit: "{work_item}");
        _failed = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsFailed, unit: "{work_item}");
        _retried = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsRetried, unit: "{work_item}");
        _duration = _meter.CreateHistogram<double>(WellKnownMetricNames.WorkItemDurationMs, unit: "ms");

        // Payload
        _fieldCount = _meter.CreateHistogram<int>(WellKnownMetricNames.FieldCount, unit: "{field}");
        _attachmentCount = _meter.CreateHistogram<int>(WellKnownMetricNames.AttachmentCount, unit: "{attachment}");
        _linkCount = _meter.CreateHistogram<int>(WellKnownMetricNames.LinkCount, unit: "{link}");
        _revisionCount = _meter.CreateHistogram<int>(WellKnownMetricNames.RevisionCount, unit: "{revision}");
        _payloadBytes = _meter.CreateHistogram<long>(WellKnownMetricNames.PayloadBytes, unit: "By");

        // Correctness
        _revisionSourceCount = _meter.CreateHistogram<int>(WellKnownMetricNames.RevisionSourceCount, unit: "{revision}");
        _revisionTargetCount = _meter.CreateHistogram<int>(WellKnownMetricNames.RevisionTargetCount, unit: "{revision}");
        _revisionDelta = _meter.CreateHistogram<int>(WellKnownMetricNames.RevisionDelta, unit: "{revision}");
        _revisionsMissing = _meter.CreateCounter<long>(WellKnownMetricNames.RevisionsMissing, unit: "{work_item}");
        _revisionOrderErrors = _meter.CreateCounter<long>(WellKnownMetricNames.RevisionOrderErrors, unit: "{work_item}");
        _brokenLinks = _meter.CreateCounter<long>(WellKnownMetricNames.BrokenLinks, unit: "{work_item}");
        _missingWorkItems = _meter.CreateCounter<long>(WellKnownMetricNames.MissingWorkItems, unit: "{work_item}");

        // In-Flight
        _inFlight = _meter.CreateUpDownCounter<int>(WellKnownMetricNames.WorkItemsInFlight, unit: "{work_item}");

        // Idempotency (deferred)
        _duplicated = _meter.CreateCounter<long>(WellKnownMetricNames.Duplicated, unit: "{work_item}");
        _changedOnRerun = _meter.CreateCounter<long>(WellKnownMetricNames.ChangedOnRerun, unit: "{work_item}");
        _reprocessedAfterResume = _meter.CreateCounter<long>(WellKnownMetricNames.ReprocessedAfterResume, unit: "{work_item}");
        _duplicatedAfterResume = _meter.CreateCounter<long>(WellKnownMetricNames.DuplicatedAfterResume, unit: "{work_item}");
        _missingAfterResume = _meter.CreateCounter<long>(WellKnownMetricNames.MissingAfterResume, unit: "{work_item}");
    }

    // --- Execution ---
    public void RecordWorkItemAttempted(in TagList tags) => _attempted.Add(1, tags);
    public void RecordWorkItemCompleted(in TagList tags) => _completed.Add(1, tags);
    public void RecordWorkItemFailed(in TagList tags) => _failed.Add(1, tags);
    public void RecordWorkItemRetried(in TagList tags) => _retried.Add(1, tags);
    public void RecordWorkItemDuration(double milliseconds, in TagList tags) => _duration.Record(milliseconds, tags);

    // --- Payload ---
    public void RecordFieldCount(int count, in TagList tags) => _fieldCount.Record(count, tags);
    public void RecordAttachmentCount(int count, in TagList tags) => _attachmentCount.Record(count, tags);
    public void RecordLinkCount(int count, in TagList tags) => _linkCount.Record(count, tags);
    public void RecordRevisionCount(int count, in TagList tags) => _revisionCount.Record(count, tags);
    public void RecordPayloadBytes(long bytes, in TagList tags) => _payloadBytes.Record(bytes, tags);

    // --- Correctness ---
    public void RecordRevisionSourceCount(int count, in TagList tags) => _revisionSourceCount.Record(count, tags);
    public void RecordRevisionTargetCount(int count, in TagList tags) => _revisionTargetCount.Record(count, tags);
    public void RecordRevisionDelta(int delta, in TagList tags) => _revisionDelta.Record(delta, tags);
    public void RecordRevisionsMissing(in TagList tags) => _revisionsMissing.Add(1, tags);
    public void RecordRevisionOrderError(in TagList tags) => _revisionOrderErrors.Add(1, tags);
    public void RecordBrokenLink(in TagList tags) => _brokenLinks.Add(1, tags);
    public void RecordMissingWorkItem(in TagList tags) => _missingWorkItems.Add(1, tags);

    // --- In-Flight ---
    public void IncrementInFlight(in TagList tags) => _inFlight.Add(1, tags);
    public void DecrementInFlight(in TagList tags) => _inFlight.Add(-1, tags);

    // --- Idempotency ---
    public void RecordDuplicated(in TagList tags) => _duplicated.Add(1, tags);
    public void RecordChangedOnRerun(in TagList tags) => _changedOnRerun.Add(1, tags);
    public void RecordReprocessedAfterResume(in TagList tags) => _reprocessedAfterResume.Add(1, tags);
    public void RecordDuplicatedAfterResume(in TagList tags) => _duplicatedAfterResume.Add(1, tags);
    public void RecordMissingAfterResume(in TagList tags) => _missingAfterResume.Add(1, tags);

    public void Dispose() => _meter.Dispose();
}
