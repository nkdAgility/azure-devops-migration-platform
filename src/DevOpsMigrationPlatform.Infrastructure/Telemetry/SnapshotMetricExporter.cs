#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// OTel SDK <see cref="BaseExporter{T}"/> that converts aggregated <see cref="Metric"/> batches
/// into a <see cref="MetricSnapshot"/> and writes it to the <see cref="IMetricSnapshotStore"/>.
/// Registered alongside OTLP and Azure Monitor exporters through a single
/// <see cref="PeriodicExportingMetricReader"/> — all exporters share the same aggregation cycle.
/// </summary>
internal sealed class SnapshotMetricExporter : BaseExporter<Metric>
{
    private readonly IMetricSnapshotStore _store;

    public SnapshotMetricExporter(IMetricSnapshotStore store)
    {
        _store = store;
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        // Execution
        long attempted = 0, completed = 0, failed = 0, retried = 0;
        double? durationMeanMs = null;

        // Payload
        double? fieldCountMean = null, attachmentCountMean = null, linkCountMean = null;
        double? revisionCountMean = null, payloadBytesMean = null;

        // Correctness
        double? revSourceMean = null, revTargetMean = null, revDeltaMean = null;
        long revMissing = 0, revOrderErrors = 0, brokenLinks = 0, missingWI = 0;

        // In-Flight
        int inFlight = 0, queueDepth = 0;

        // Idempotency (nullable — null when no measurements recorded)
        long? duplicated = null, changedOnRerun = null;
        long? reprocessedAfterResume = null, duplicatedAfterResume = null, missingAfterResume = null;

        foreach (var metric in batch)
        {
            switch (metric.Name)
            {
                // --- Execution ---
                case WellKnownMetricNames.WorkItemsAttempted:
                    attempted = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.WorkItemsCompleted:
                    completed = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.WorkItemsFailed:
                    failed = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.WorkItemsRetried:
                    retried = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.WorkItemDurationMs:
                    durationMeanMs = ReadHistogramMean(metric);
                    break;

                // --- Payload ---
                case WellKnownMetricNames.FieldCount:
                    fieldCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.AttachmentCount:
                    attachmentCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.LinkCount:
                    linkCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.RevisionCount:
                    revisionCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.PayloadBytes:
                    payloadBytesMean = ReadHistogramMean(metric);
                    break;

                // --- Correctness ---
                case WellKnownMetricNames.RevisionSourceCount:
                    revSourceMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.RevisionTargetCount:
                    revTargetMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.RevisionDelta:
                    revDeltaMean = ReadHistogramMean(metric);
                    break;
                case WellKnownMetricNames.RevisionsMissing:
                    revMissing = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.RevisionOrderErrors:
                    revOrderErrors = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.BrokenLinks:
                    brokenLinks = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.MissingWorkItems:
                    missingWI = ReadCounterSum(metric);
                    break;

                // --- In-Flight ---
                case WellKnownMetricNames.WorkItemsInFlight:
                    inFlight = (int)ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.QueueDepth:
                    queueDepth = ReadGaugeLatest(metric);
                    break;

                // --- Idempotency ---
                case WellKnownMetricNames.Duplicated:
                    duplicated = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.ChangedOnRerun:
                    changedOnRerun = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.ReprocessedAfterResume:
                    reprocessedAfterResume = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.DuplicatedAfterResume:
                    duplicatedAfterResume = ReadCounterSum(metric);
                    break;
                case WellKnownMetricNames.MissingAfterResume:
                    missingAfterResume = ReadCounterSum(metric);
                    break;
            }
        }

        _store.Update(new MetricSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            WorkItemsAttempted = attempted,
            WorkItemsCompleted = completed,
            WorkItemsFailed = failed,
            WorkItemsRetried = retried,
            WorkItemDurationMeanMs = durationMeanMs,
            FieldCountMean = fieldCountMean,
            AttachmentCountMean = attachmentCountMean,
            LinkCountMean = linkCountMean,
            RevisionCountMean = revisionCountMean,
            PayloadBytesMean = payloadBytesMean,
            RevisionSourceCountMean = revSourceMean,
            RevisionTargetCountMean = revTargetMean,
            RevisionDeltaMean = revDeltaMean,
            RevisionsMissing = revMissing,
            RevisionOrderErrors = revOrderErrors,
            BrokenLinks = brokenLinks,
            MissingWorkItems = missingWI,
            WorkItemsInFlight = inFlight,
            QueueDepth = queueDepth,
            Duplicated = duplicated,
            ChangedOnRerun = changedOnRerun,
            ReprocessedAfterResume = reprocessedAfterResume,
            DuplicatedAfterResume = duplicatedAfterResume,
            MissingAfterResume = missingAfterResume,
        });

        return ExportResult.Success;
    }

    // Sums all MetricPoints for a cumulative or delta counter.
    private static long ReadCounterSum(Metric metric)
    {
        long sum = 0;
        foreach (ref readonly MetricPoint mp in metric.GetMetricPoints())
            sum += mp.GetSumLong();
        return sum;
    }

    // Returns sum(count * sum) / total_count across all MetricPoints for a histogram,
    // providing a weighted mean. Returns null when no measurements have been recorded.
    private static double? ReadHistogramMean(Metric metric)
    {
        double totalSum = 0;
        long totalCount = 0;

        foreach (ref readonly MetricPoint mp in metric.GetMetricPoints())
        {
            totalSum += mp.GetHistogramSum();
            totalCount += mp.GetHistogramCount();
        }

        return totalCount > 0 ? totalSum / totalCount : null;
    }

    // Reads the latest reported value from an ObservableGauge instrument.
    // The SDK invokes the gauge callback on each collection cycle and reports
    // the value as a sum across MetricPoints.
    private static int ReadGaugeLatest(Metric metric)
    {
        long latest = 0;
        foreach (ref readonly MetricPoint mp in metric.GetMetricPoints())
            latest = mp.GetSumLong();
        return (int)latest;
    }
}
#endif
