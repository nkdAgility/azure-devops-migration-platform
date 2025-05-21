using MigrationPlatform.Abstractions.Telemetry;
using System.Diagnostics.Metrics;

namespace MigrationPlatform.Infrastructure.Telemetry
{
    public class WorkItemExportMetrics : IWorkItemExportMetrics
    {
        public const string MeterName = "MigrationPlatform.WorkItemExport";
        public const string MeterVersion = "1.0";

        private static readonly Meter Meter = new Meter(MeterName, MeterVersion);

        private static readonly Counter<int> WorkItemCounter =
            Meter.CreateCounter<int>("work_item_exported_total", unit: "work items", description: "Total number of work items exported.");

        private static readonly Counter<int> RevisionCounter =
            Meter.CreateCounter<int>("revision_exported_total", unit: "revisions", description: "Total number of work item revisions exported.");

        private static readonly Counter<int> RevisionErrorCounter =
            Meter.CreateCounter<int>("revision_export_errors_total", unit: "errors", description: "Total number of errors encountered during revision export.");

        private static readonly Counter<int> LinkCounter =
            Meter.CreateCounter<int>("link_exported_total", unit: "links", description: "Total number of work item links exported.");

        private static readonly Counter<int> LinkErrorCounter =
            Meter.CreateCounter<int>("link_export_errors_total", unit: "errors", description: "Total number of errors encountered during link export.");

        private static readonly Histogram<double> WorkItemProcessingDuration =
            Meter.CreateHistogram<double>("work_item_export_duration_ms", unit: "ms", description: "Duration of exporting a work item in milliseconds.");

        private static readonly Histogram<double> RevisionProcessingDuration =
            Meter.CreateHistogram<double>("revision_export_duration_ms", unit: "ms", description: "Duration of exporting a revision in milliseconds.");

        private static readonly Histogram<double> LinkProcessingDuration =
            Meter.CreateHistogram<double>("link_export_duration_ms", unit: "ms", description: "Duration of exporting a link in milliseconds.");

        private static readonly Histogram<double> ExportProcessingDuration =
            Meter.CreateHistogram<double>("export_total_duration_ms", unit: "ms", description: "Total duration of the work item export operation in milliseconds.");

        public void RecordWorkItemExported(Guid teamProjectCollectionId)
        {
            WorkItemCounter.Add(1, new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId));
        }

        public void RecordRevisionExported(Guid teamProjectCollectionId, int workItemId)
        {
            RevisionCounter.Add(1,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId),
                new KeyValuePair<string, object?>("WorkItemId", workItemId));
        }

        public void RecordLinkExported(Guid teamProjectCollectionId, int workItemId, int revisionIndex)
        {
            LinkCounter.Add(1,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId),
                new KeyValuePair<string, object?>("WorkItemId", workItemId),
                new KeyValuePair<string, object?>("RevisionIndex", revisionIndex));
        }

        public void RecordWorkItemProcessingDuration(Guid teamProjectCollectionId, TimeSpan duration)
        {
            WorkItemProcessingDuration.Record(duration.TotalMilliseconds,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId));
        }

        public void RecordRevisionProcessingDuration(Guid teamProjectCollectionId, int workItemId, TimeSpan duration)
        {
            RevisionProcessingDuration.Record(duration.TotalMilliseconds,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId),
                new KeyValuePair<string, object?>("WorkItemId", workItemId));
        }

        public void RecordLinkProcessingDuration(Guid teamProjectCollectionId, int workItemId, int revisionIndex, TimeSpan duration)
        {
            LinkProcessingDuration.Record(duration.TotalMilliseconds,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId),
                new KeyValuePair<string, object?>("WorkItemId", workItemId),
                new KeyValuePair<string, object?>("RevisionIndex", revisionIndex));
        }

        public void RecordProcessingDuration(TimeSpan duration)
        {
            ExportProcessingDuration.Record(duration.TotalMilliseconds);
        }

        public void RecordRevisionError(Guid teamProjectCollectionId, int workItemId)
        {
            RevisionErrorCounter.Add(1,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId),
                new KeyValuePair<string, object?>("WorkItemId", workItemId));
        }

        public void RecordLinkError(Guid teamProjectCollectionId, int workItemId, int revisionIndex)
        {
            LinkErrorCounter.Add(1,
                new KeyValuePair<string, object?>("TeamProjectCollectionId", teamProjectCollectionId),
                new KeyValuePair<string, object?>("WorkItemId", workItemId),
                new KeyValuePair<string, object?>("RevisionIndex", revisionIndex));
        }
    }
}
