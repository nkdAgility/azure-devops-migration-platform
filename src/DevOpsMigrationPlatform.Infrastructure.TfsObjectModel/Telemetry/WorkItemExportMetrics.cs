// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

/// <summary>
/// Work item export metrics emitted under the consolidated <see cref="WellKnownMeterNames.Agent"/> meter.
/// Used by the net481 TFS subprocess where <c>IPlatformMetrics</c> (which requires <c>TagList</c>) is unavailable.
/// </summary>
public class WorkItemExportMetrics : IWorkItemExportMetrics
{
    // Inline metric names because WellKnownMetricNames constants are not used in this net481 path.
    private const string WorkItemsExportedName = "work_item_exported_total";
    private const string RevisionsExportedName = "revision_exported_total";
    private const string RevisionErrorsName = "revision_export_errors_total";
    private const string LinksExportedName = "link_exported_total";
    private const string LinkErrorsName = "link_export_errors_total";
    private const string WorkItemDurationName = "work_item_export_duration_ms";
    private const string RevisionDurationName = "revision_export_duration_ms";
    private const string LinkDurationName = "link_export_duration_ms";
    private const string TotalDurationName = "export_total_duration_ms";

    public const string MeterName = WellKnownMeterNames.Agent;
    public const string MeterVersion = "1.0";

    private static readonly Meter Meter = new Meter(MeterName, MeterVersion);

    private static readonly Counter<int> WorkItemCounter =
        Meter.CreateCounter<int>(WorkItemsExportedName, unit: "work items", description: "Total work items exported.");

    private static readonly Counter<int> RevisionCounter =
        Meter.CreateCounter<int>(RevisionsExportedName, unit: "revisions", description: "Total revisions exported.");

    private static readonly Counter<int> RevisionErrorCounter =
        Meter.CreateCounter<int>(RevisionErrorsName, unit: "errors", description: "Export revision errors.");

    private static readonly Counter<int> LinkCounter =
        Meter.CreateCounter<int>(LinksExportedName, unit: "links", description: "Total links exported.");

    private static readonly Counter<int> LinkErrorCounter =
        Meter.CreateCounter<int>(LinkErrorsName, unit: "errors", description: "Export link errors.");

    private static readonly Histogram<double> WorkItemDuration =
        Meter.CreateHistogram<double>(WorkItemDurationName, unit: "ms");

    private static readonly Histogram<double> RevisionDuration =
        Meter.CreateHistogram<double>(RevisionDurationName, unit: "ms");

    private static readonly Histogram<double> LinkDuration =
        Meter.CreateHistogram<double>(LinkDurationName, unit: "ms");

    private static readonly Histogram<double> TotalDuration =
        Meter.CreateHistogram<double>(TotalDurationName, unit: "ms");

    public void RecordWorkItemExported(Guid id) =>
        WorkItemCounter.Add(1);

    public void RecordRevisionExported(Guid id, int workItemId) =>
        RevisionCounter.Add(1);

    public void RecordWorkItemProcessingDuration(Guid id, TimeSpan duration) =>
        WorkItemDuration.Record(duration.TotalMilliseconds);

    public void RecordRevisionProcessingDuration(Guid id, int workItemId, TimeSpan duration) =>
        RevisionDuration.Record(duration.TotalMilliseconds);

    public void RecordProcessingDuration(TimeSpan duration) =>
        TotalDuration.Record(duration.TotalMilliseconds);

    public void RecordRevisionError(Guid id, int workItemId) =>
        RevisionErrorCounter.Add(1);

    public void RecordLinkExported(Guid id, int workItemId, int revisionIndex) =>
        LinkCounter.Add(1);

    public void RecordLinkProcessingDuration(Guid id, int workItemId, int revisionIndex, TimeSpan duration) =>
        LinkDuration.Record(duration.TotalMilliseconds);

    public void RecordLinkError(Guid id, int workItemId, int revisionIndex) =>
        LinkErrorCounter.Add(1);
}
