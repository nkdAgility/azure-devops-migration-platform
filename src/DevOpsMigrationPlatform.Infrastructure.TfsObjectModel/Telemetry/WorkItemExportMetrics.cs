using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

public class WorkItemExportMetrics : IWorkItemExportMetrics
{
    public const string MeterName = WellKnownMeterNames.WorkItemExport;
    public const string MeterVersion = "1.0";

    private static readonly Meter Meter = new Meter(MeterName, MeterVersion);

    private static readonly Counter<int> WorkItemCounter =
        Meter.CreateCounter<int>(WellKnownMetricNames.WorkItemsExported, unit: "work items", description: "Total work items exported.");

    private static readonly Counter<int> RevisionCounter =
        Meter.CreateCounter<int>(WellKnownMetricNames.RevisionsExported, unit: "revisions", description: "Total revisions exported.");

    private static readonly Counter<int> RevisionErrorCounter =
        Meter.CreateCounter<int>(WellKnownMetricNames.RevisionErrors, unit: "errors", description: "Export revision errors.");

    private static readonly Counter<int> LinkCounter =
        Meter.CreateCounter<int>(WellKnownMetricNames.LinksExported, unit: "links", description: "Total links exported.");

    private static readonly Counter<int> LinkErrorCounter =
        Meter.CreateCounter<int>(WellKnownMetricNames.LinkErrors, unit: "errors", description: "Export link errors.");

    private static readonly Histogram<double> WorkItemDuration =
        Meter.CreateHistogram<double>(WellKnownMetricNames.WorkItemDuration, unit: "ms");

    private static readonly Histogram<double> RevisionDuration =
        Meter.CreateHistogram<double>(WellKnownMetricNames.RevisionDuration, unit: "ms");

    private static readonly Histogram<double> LinkDuration =
        Meter.CreateHistogram<double>(WellKnownMetricNames.LinkDuration, unit: "ms");

    private static readonly Histogram<double> TotalDuration =
        Meter.CreateHistogram<double>(WellKnownMetricNames.TotalDuration, unit: "ms");

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
