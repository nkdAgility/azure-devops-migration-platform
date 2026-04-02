using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

public class WorkItemExportMetrics : IWorkItemExportMetrics
{
    public const string MeterName = "DevOpsMigrationPlatform.WorkItemExport";
    public const string MeterVersion = "1.0";

    private static readonly Meter Meter = new Meter(MeterName, MeterVersion);

    private static readonly Counter<int> WorkItemCounter =
        Meter.CreateCounter<int>("work_item_exported_total", unit: "work items", description: "Total work items exported.");

    private static readonly Counter<int> RevisionCounter =
        Meter.CreateCounter<int>("revision_exported_total", unit: "revisions", description: "Total revisions exported.");

    private static readonly Counter<int> RevisionErrorCounter =
        Meter.CreateCounter<int>("revision_export_errors_total", unit: "errors", description: "Export revision errors.");

    private static readonly Counter<int> LinkCounter =
        Meter.CreateCounter<int>("link_exported_total", unit: "links", description: "Total links exported.");

    private static readonly Counter<int> LinkErrorCounter =
        Meter.CreateCounter<int>("link_export_errors_total", unit: "errors", description: "Export link errors.");

    private static readonly Histogram<double> WorkItemDuration =
        Meter.CreateHistogram<double>("work_item_export_duration_ms", unit: "ms");

    private static readonly Histogram<double> RevisionDuration =
        Meter.CreateHistogram<double>("revision_export_duration_ms", unit: "ms");

    private static readonly Histogram<double> LinkDuration =
        Meter.CreateHistogram<double>("link_export_duration_ms", unit: "ms");

    private static readonly Histogram<double> TotalDuration =
        Meter.CreateHistogram<double>("export_total_duration_ms", unit: "ms");

    public void RecordWorkItemExported(Guid id) =>
        WorkItemCounter.Add(1, new KeyValuePair<string, object?>("TeamProjectCollectionId", id));

    public void RecordRevisionExported(Guid id, int workItemId) =>
        RevisionCounter.Add(1,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id),
            new KeyValuePair<string, object?>("WorkItemId", workItemId));

    public void RecordWorkItemProcessingDuration(Guid id, TimeSpan duration) =>
        WorkItemDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id));

    public void RecordRevisionProcessingDuration(Guid id, int workItemId, TimeSpan duration) =>
        RevisionDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id),
            new KeyValuePair<string, object?>("WorkItemId", workItemId));

    public void RecordProcessingDuration(TimeSpan duration) =>
        TotalDuration.Record(duration.TotalMilliseconds);

    public void RecordRevisionError(Guid id, int workItemId) =>
        RevisionErrorCounter.Add(1,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id),
            new KeyValuePair<string, object?>("WorkItemId", workItemId));

    public void RecordLinkExported(Guid id, int workItemId, int revisionIndex) =>
        LinkCounter.Add(1,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id),
            new KeyValuePair<string, object?>("WorkItemId", workItemId),
            new KeyValuePair<string, object?>("RevisionIndex", revisionIndex));

    public void RecordLinkProcessingDuration(Guid id, int workItemId, int revisionIndex, TimeSpan duration) =>
        LinkDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id),
            new KeyValuePair<string, object?>("WorkItemId", workItemId),
            new KeyValuePair<string, object?>("RevisionIndex", revisionIndex));

    public void RecordLinkError(Guid id, int workItemId, int revisionIndex) =>
        LinkErrorCounter.Add(1,
            new KeyValuePair<string, object?>("TeamProjectCollectionId", id),
            new KeyValuePair<string, object?>("WorkItemId", workItemId),
            new KeyValuePair<string, object?>("RevisionIndex", revisionIndex));
}
