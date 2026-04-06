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
    // Instrument names from WorkItemExportMetrics and AttachmentDownloadMetrics.
    // Use centralized constants from WellKnownMetricNames for Application Insights contract stability.

    private readonly IMetricSnapshotStore _store;

    public SnapshotMetricExporter(IMetricSnapshotStore store)
    {
        _store = store;
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        long workItemsExported = 0;
        long revisionsExported = 0;
        long revisionErrors = 0;
        long linksExported = 0;
        long linkErrors = 0;
        long attachmentsAttempted = 0;
        long attachmentsSucceeded = 0;
        long attachmentsFailed = 0;
        double? workItemMeanMs = null;
        double? revisionMeanMs = null;
        double? totalDurationMs = null;

        foreach (var metric in batch)
        {
            switch (metric.Name)
            {
                case WellKnownMetricNames.WorkItemsExported:
                    workItemsExported = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.RevisionsExported:
                    revisionsExported = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.RevisionErrors:
                    revisionErrors = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.LinksExported:
                    linksExported = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.LinkErrors:
                    linkErrors = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.AttachmentAttempts:
                    attachmentsAttempted = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.AttachmentSuccesses:
                    attachmentsSucceeded = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.AttachmentFailures:
                    attachmentsFailed = ReadCounterSum(metric);
                    break;

                case WellKnownMetricNames.WorkItemDuration:
                    workItemMeanMs = ReadHistogramMean(metric);
                    break;

                case WellKnownMetricNames.RevisionDuration:
                    revisionMeanMs = ReadHistogramMean(metric);
                    break;

                case WellKnownMetricNames.TotalDuration:
                    totalDurationMs = ReadHistogramMean(metric);
                    break;
            }
        }

        _store.Update(new MetricSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            WorkItemsExported = workItemsExported,
            RevisionsExported = revisionsExported,
            RevisionErrors = revisionErrors,
            LinksExported = linksExported,
            LinkErrors = linkErrors,
            AttachmentsAttempted = attachmentsAttempted,
            AttachmentsSucceeded = attachmentsSucceeded,
            AttachmentsFailed = attachmentsFailed,
            WorkItemDurationMeanMs = workItemMeanMs,
            RevisionDurationMeanMs = revisionMeanMs,
            TotalExportDurationMs = totalDurationMs,
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
}
#endif
