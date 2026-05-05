// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

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
    private readonly Histogram<double> _attachmentDownloadDuration;
    private readonly Histogram<long> _attachmentDownloadBytes;
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

    // --- FieldTransform ---
    private readonly Counter<long> _fieldTransformApplied;
    private readonly Histogram<double> _fieldTransformDuration;
    private readonly Counter<long> _fieldTransformErrors;
    private readonly UpDownCounter<long> _fieldTransformInFlight;
    private readonly Histogram<int> _fieldTransformFieldsModified;

    // --- Idempotency (deferred) ---
    private readonly Counter<long> _duplicated;
    private readonly Counter<long> _changedOnRerun;
    private readonly Counter<long> _reprocessedAfterResume;
    private readonly Counter<long> _duplicatedAfterResume;
    private readonly Counter<long> _missingAfterResume;

    // --- NodeTranslation Export ---
    private readonly Histogram<int> _nodeExportTreeCount;
    private readonly Histogram<double> _nodeExportTreeDuration;
    private readonly Counter<long> _nodeExportTreeErrors;

    // --- NodeTranslation Translate ---
    private readonly Counter<long> _nodeTranslateCount;
    private readonly Counter<long> _nodeTranslateMapHit;
    private readonly Counter<long> _nodeTranslateAutoSwapHit;
    private readonly Counter<long> _nodeTranslateExternal;
    private readonly Counter<long> _nodeTranslateUnresolvable;

    // --- NodeTranslation Import: Replicate ---
    private readonly Counter<long> _nodeImportReplicateCount;
    private readonly Counter<long> _nodeImportReplicateAreaCount;
    private readonly Counter<long> _nodeImportReplicateIterationCount;
    private readonly Histogram<double> _nodeImportReplicateDuration;
    private readonly Counter<long> _nodeImportReplicateErrors;
    private readonly Counter<long> _nodeImportReplicateSkipped;
    private readonly UpDownCounter<long> _nodeImportReplicateInFlight;

    // --- NodeTranslation Import: PreCollect ---
    private readonly Counter<long> _nodeImportPreCollectCount;
    private readonly Histogram<double> _nodeImportPreCollectDuration;
    private readonly Counter<long> _nodeImportPreCollectErrors;
    private readonly UpDownCounter<long> _nodeImportPreCollectInFlight;

    // --- Teams Export ---
    private readonly Counter<long> _teamExportCount;
    private readonly Histogram<double> _teamExportDuration;
    private readonly Counter<long> _teamExportErrors;
    private readonly UpDownCounter<long> _teamExportInFlight;

    // --- Teams Import ---
    private readonly Counter<long> _teamImportCount;
    private readonly Histogram<double> _teamImportDuration;
    private readonly Counter<long> _teamImportErrors;
    private readonly UpDownCounter<long> _teamImportInFlight;
    private readonly Counter<long> _teamImportMembersCount;
    private readonly Counter<long> _teamImportMembersUnresolved;
    private readonly Counter<long> _teamImportIterationsCount;
    private readonly Counter<long> _teamImportIterationsUnresolvable;
    private readonly Counter<long> _teamImportCapacityCount;
    private readonly Histogram<double> _teamImportExtensionDuration;

    // --- Teams Validate ---
    private readonly Counter<long> _teamValidateCount;
    private readonly Counter<long> _teamValidateErrors;

    // --- Identities Export ---
    private readonly Counter<long> _identityExportCount;
    private readonly Histogram<double> _identityExportDuration;
    private readonly Counter<long> _identityExportErrors;
    private readonly UpDownCounter<long> _identityExportInFlight;

    // --- Identities Import ---
    private readonly Counter<long> _identityImportResolved;
    private readonly Counter<long> _identityImportUnresolved;
    private readonly Histogram<double> _identityImportDuration;
    private readonly Counter<long> _identityImportErrors;

    // --- Identities Validate ---
    private readonly Counter<long> _identityValidateCount;
    private readonly Counter<long> _identityValidateErrors;

    // --- Prepare ---
    private readonly Counter<long> _prepareWorkItemsResolved;
    private readonly Counter<long> _prepareWorkItemsUnresolved;
    private readonly Histogram<double> _prepareWorkItemsDuration;
    private readonly Counter<long> _prepareWorkItemsErrors;
    private readonly UpDownCounter<long> _prepareWorkItemsInFlight;
    private readonly Counter<long> _prepareIdentitiesResolved;
    private readonly Counter<long> _prepareIdentitiesUnresolved;
    private readonly Histogram<double> _prepareIdentitiesDuration;
    private readonly Counter<long> _prepareIdentitiesErrors;
    private readonly UpDownCounter<long> _prepareIdentitiesInFlight;
    private readonly Counter<long> _prepareNodesResolved;
    private readonly Counter<long> _prepareNodesUnresolved;
    private readonly Histogram<double> _prepareNodesDuration;
    private readonly Counter<long> _prepareNodesErrors;
    private readonly UpDownCounter<long> _prepareNodesInFlight;
    private readonly Counter<long> _prepareTeamsResolved;
    private readonly Counter<long> _prepareTeamsUnresolved;
    private readonly Histogram<double> _prepareTeamsDuration;
    private readonly Counter<long> _prepareTeamsErrors;
    private readonly UpDownCounter<long> _prepareTeamsInFlight;

    // --- Package Config ---
    private readonly Counter<long> _configWriteCount;
    private readonly Counter<long> _configWriteErrors;
    private readonly Counter<long> _configReadCount;
    private readonly Counter<long> _configReadErrors;
    private readonly Counter<long> _configReadFallbacks;

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
        _attachmentDownloadDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.AttachmentDownloadDurationMs, unit: "ms");
        _attachmentDownloadBytes = _meter.CreateHistogram<long>(WellKnownMetricNames.AttachmentDownloadBytes, unit: "By");
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

        // FieldTransform
        _fieldTransformApplied = _meter.CreateCounter<long>(WellKnownMetricNames.FieldTransformApplyCount, unit: "{revision}");
        _fieldTransformDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.FieldTransformApplyDurationMs, unit: "ms");
        _fieldTransformErrors = _meter.CreateCounter<long>(WellKnownMetricNames.FieldTransformApplyErrors, unit: "{revision}");
        _fieldTransformInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.FieldTransformApplyInFlight, unit: "{revision}");
        _fieldTransformFieldsModified = _meter.CreateHistogram<int>(WellKnownMetricNames.FieldTransformApplyFieldsModified, unit: "{field}");

        // Idempotency (deferred)
        _duplicated = _meter.CreateCounter<long>(WellKnownMetricNames.Duplicated, unit: "{work_item}");
        _changedOnRerun = _meter.CreateCounter<long>(WellKnownMetricNames.ChangedOnRerun, unit: "{work_item}");
        _reprocessedAfterResume = _meter.CreateCounter<long>(WellKnownMetricNames.ReprocessedAfterResume, unit: "{work_item}");
        _duplicatedAfterResume = _meter.CreateCounter<long>(WellKnownMetricNames.DuplicatedAfterResume, unit: "{work_item}");
        _missingAfterResume = _meter.CreateCounter<long>(WellKnownMetricNames.MissingAfterResume, unit: "{work_item}");

        // NodeTranslation Export
        _nodeExportTreeCount = _meter.CreateHistogram<int>(WellKnownMetricNames.NodeExportTreeCount, unit: "{node}");
        _nodeExportTreeDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.NodeExportTreeDurationMs, unit: "ms");
        _nodeExportTreeErrors = _meter.CreateCounter<long>(WellKnownMetricNames.NodeExportTreeErrors, unit: "{error}");

        // NodeTranslation Translate
        _nodeTranslateCount = _meter.CreateCounter<long>(WellKnownMetricNames.NodeTranslateCount, unit: "{path}");
        _nodeTranslateMapHit = _meter.CreateCounter<long>(WellKnownMetricNames.NodeTranslateMapHit, unit: "{path}");
        _nodeTranslateAutoSwapHit = _meter.CreateCounter<long>(WellKnownMetricNames.NodeTranslateAutoSwapHit, unit: "{path}");
        _nodeTranslateExternal = _meter.CreateCounter<long>(WellKnownMetricNames.NodeTranslateExternal, unit: "{path}");
        _nodeTranslateUnresolvable = _meter.CreateCounter<long>(WellKnownMetricNames.NodeTranslateUnresolvable, unit: "{path}");

        // NodeTranslation Import: Replicate
        _nodeImportReplicateCount = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportReplicateCount, unit: "{node}");
        _nodeImportReplicateAreaCount = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportReplicateAreaCount, unit: "{node}");
        _nodeImportReplicateIterationCount = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportReplicateIterationCount, unit: "{node}");
        _nodeImportReplicateDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.NodeImportReplicateDurationMs, unit: "ms");
        _nodeImportReplicateErrors = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportReplicateErrors, unit: "{error}");
        _nodeImportReplicateSkipped = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportReplicateSkipped, unit: "{node}");
        _nodeImportReplicateInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.NodeImportReplicateInFlight, unit: "{node}");

        // NodeTranslation Import: PreCollect
        _nodeImportPreCollectCount = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportPreCollectCount, unit: "{node}");
        _nodeImportPreCollectDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.NodeImportPreCollectDurationMs, unit: "ms");
        _nodeImportPreCollectErrors = _meter.CreateCounter<long>(WellKnownMetricNames.NodeImportPreCollectErrors, unit: "{error}");
        _nodeImportPreCollectInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.NodeImportPreCollectInFlight, unit: "{node}");

        // Teams Export
        _teamExportCount = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsExportCount, unit: "{team}");
        _teamExportDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.TeamsExportDurationMs, unit: "ms");
        _teamExportErrors = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsExportErrors, unit: "{error}");
        _teamExportInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.TeamsExportInFlight, unit: "{team}");

        // Teams Import
        _teamImportCount = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportCount, unit: "{team}");
        _teamImportDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.TeamsImportDurationMs, unit: "ms");
        _teamImportErrors = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportErrors, unit: "{error}");
        _teamImportInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.TeamsImportInFlight, unit: "{team}");
        _teamImportMembersCount = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportMembersCount, unit: "{member}");
        _teamImportMembersUnresolved = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportMembersUnresolved, unit: "{member}");
        _teamImportIterationsCount = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportIterationsCount, unit: "{iteration}");
        _teamImportIterationsUnresolvable = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportIterationsUnresolvable, unit: "{iteration}");
        _teamImportCapacityCount = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsImportCapacityCount, unit: "{entry}");
        _teamImportExtensionDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.TeamsImportExtensionDurationMs, unit: "ms");

        // Teams Validate
        _teamValidateCount = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsValidateCount, unit: "{team}");
        _teamValidateErrors = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsValidateErrors, unit: "{error}");

        // Identities Export
        _identityExportCount = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesExportCount, unit: "{identity}");
        _identityExportDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.IdentitiesExportDurationMs, unit: "ms");
        _identityExportErrors = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesExportErrors, unit: "{error}");
        _identityExportInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.IdentitiesExportInFlight, unit: "{identity}");

        // Identities Import
        _identityImportResolved = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesImportResolved, unit: "{identity}");
        _identityImportUnresolved = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesImportUnresolved, unit: "{identity}");
        _identityImportDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.IdentitiesImportDurationMs, unit: "ms");
        _identityImportErrors = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesImportErrors, unit: "{error}");

        // Identities Validate
        _identityValidateCount = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesValidateCount, unit: "{identity}");
        _identityValidateErrors = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesValidateErrors, unit: "{error}");

        // Prepare
        _prepareWorkItemsResolved = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsPrepareResolved, unit: "{item}");
        _prepareWorkItemsUnresolved = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsPrepareUnresolved, unit: "{item}");
        _prepareWorkItemsDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.WorkItemsPrepareDurationMs, unit: "ms");
        _prepareWorkItemsErrors = _meter.CreateCounter<long>(WellKnownMetricNames.WorkItemsPrepareErrors, unit: "{error}");
        _prepareWorkItemsInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.WorkItemsPrepareInFlight, unit: "{item}");
        _prepareIdentitiesResolved = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesPrepareResolved, unit: "{item}");
        _prepareIdentitiesUnresolved = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesPrepareUnresolved, unit: "{item}");
        _prepareIdentitiesDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.IdentitiesPrepareDurationMs, unit: "ms");
        _prepareIdentitiesErrors = _meter.CreateCounter<long>(WellKnownMetricNames.IdentitiesPrepareErrors, unit: "{error}");
        _prepareIdentitiesInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.IdentitiesPrepareInFlight, unit: "{item}");
        _prepareNodesResolved = _meter.CreateCounter<long>(WellKnownMetricNames.NodesPrepareResolved, unit: "{item}");
        _prepareNodesUnresolved = _meter.CreateCounter<long>(WellKnownMetricNames.NodesPrepareUnresolved, unit: "{item}");
        _prepareNodesDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.NodesPrepareDurationMs, unit: "ms");
        _prepareNodesErrors = _meter.CreateCounter<long>(WellKnownMetricNames.NodesPrepareErrors, unit: "{error}");
        _prepareNodesInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.NodesPrepareInFlight, unit: "{item}");
        _prepareTeamsResolved = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsPrepareResolved, unit: "{item}");
        _prepareTeamsUnresolved = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsPrepareUnresolved, unit: "{item}");
        _prepareTeamsDuration = _meter.CreateHistogram<double>(WellKnownMetricNames.TeamsPrepareDurationMs, unit: "ms");
        _prepareTeamsErrors = _meter.CreateCounter<long>(WellKnownMetricNames.TeamsPrepareErrors, unit: "{error}");
        _prepareTeamsInFlight = _meter.CreateUpDownCounter<long>(WellKnownMetricNames.TeamsPrepareInFlight, unit: "{item}");

        // Package Config
        _configWriteCount = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigWriteCount, unit: "{operation}");
        _configWriteErrors = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigWriteErrors, unit: "{error}");
        _configReadCount = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigReadCount, unit: "{operation}");
        _configReadErrors = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigReadErrors, unit: "{error}");
        _configReadFallbacks = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigReadFallbacks, unit: "{fallback}");
    }

    /// <summary>
    /// Converts the portable <see cref="IReadOnlyList{T}"/> tag representation into an OTel
    /// <see cref="TagList"/> for zero-overhead passing to instrument recording methods.
    /// TagList is kept as a private OTel implementation detail; it never appears on public boundaries.
    /// </summary>
    private static TagList ToTagList(MetricsTagList tags)
    {
        var tagList = new TagList();
        for (var i = 0; i < tags.Count; i++)
            tagList.Add(tags[i].Key, tags[i].Value);
        return tagList;
    }

    public void RecordWorkItemAttempted(MetricsTagList tags) => _attempted.Add(1, ToTagList(tags));
    public void RecordWorkItemCompleted(MetricsTagList tags) => _completed.Add(1, ToTagList(tags));
    public void RecordWorkItemFailed(MetricsTagList tags) => _failed.Add(1, ToTagList(tags));
    public void RecordWorkItemRetried(MetricsTagList tags) => _retried.Add(1, ToTagList(tags));
    public void RecordWorkItemDuration(double milliseconds, MetricsTagList tags) => _duration.Record(milliseconds, ToTagList(tags));

    // --- Payload ---
    public void RecordFieldCount(int count, MetricsTagList tags) => _fieldCount.Record(count, ToTagList(tags));
    public void RecordAttachmentCount(int count, MetricsTagList tags) => _attachmentCount.Record(count, ToTagList(tags));
    public void RecordAttachmentDownloadDuration(double milliseconds, MetricsTagList tags) => _attachmentDownloadDuration.Record(milliseconds, ToTagList(tags));
    public void RecordAttachmentDownloadBytes(long bytes, MetricsTagList tags) => _attachmentDownloadBytes.Record(bytes, ToTagList(tags));
    public void RecordLinkCount(int count, MetricsTagList tags) => _linkCount.Record(count, ToTagList(tags));
    public void RecordRevisionCount(int count, MetricsTagList tags) => _revisionCount.Record(count, ToTagList(tags));
    public void RecordPayloadBytes(long bytes, MetricsTagList tags) => _payloadBytes.Record(bytes, ToTagList(tags));

    // --- Correctness ---
    public void RecordRevisionSourceCount(int count, MetricsTagList tags) => _revisionSourceCount.Record(count, ToTagList(tags));
    public void RecordRevisionTargetCount(int count, MetricsTagList tags) => _revisionTargetCount.Record(count, ToTagList(tags));
    public void RecordRevisionDelta(int delta, MetricsTagList tags) => _revisionDelta.Record(delta, ToTagList(tags));
    public void RecordRevisionsMissing(MetricsTagList tags) => _revisionsMissing.Add(1, ToTagList(tags));
    public void RecordRevisionOrderError(MetricsTagList tags) => _revisionOrderErrors.Add(1, ToTagList(tags));
    public void RecordBrokenLink(MetricsTagList tags) => _brokenLinks.Add(1, ToTagList(tags));
    public void RecordMissingWorkItem(MetricsTagList tags) => _missingWorkItems.Add(1, ToTagList(tags));

    // --- In-Flight ---
    public void IncrementInFlight(MetricsTagList tags) => _inFlight.Add(1, ToTagList(tags));
    public void DecrementInFlight(MetricsTagList tags) => _inFlight.Add(-1, ToTagList(tags));

    // --- FieldTransform ---
    public void RecordFieldTransformApplied(MetricsTagList tags) => _fieldTransformApplied.Add(1, ToTagList(tags));
    public void RecordFieldTransformDuration(double milliseconds, MetricsTagList tags) => _fieldTransformDuration.Record(milliseconds, ToTagList(tags));
    public void RecordFieldTransformError(MetricsTagList tags) => _fieldTransformErrors.Add(1, ToTagList(tags));
    public void IncrementFieldTransformInFlight(MetricsTagList tags) => _fieldTransformInFlight.Add(1, ToTagList(tags));
    public void DecrementFieldTransformInFlight(MetricsTagList tags) => _fieldTransformInFlight.Add(-1, ToTagList(tags));
    public void RecordFieldTransformFieldsModified(int count, MetricsTagList tags) => _fieldTransformFieldsModified.Record(count, ToTagList(tags));

    // --- Idempotency ---
    public void RecordDuplicated(MetricsTagList tags) => _duplicated.Add(1, ToTagList(tags));
    public void RecordChangedOnRerun(MetricsTagList tags) => _changedOnRerun.Add(1, ToTagList(tags));
    public void RecordReprocessedAfterResume(MetricsTagList tags) => _reprocessedAfterResume.Add(1, ToTagList(tags));
    public void RecordDuplicatedAfterResume(MetricsTagList tags) => _duplicatedAfterResume.Add(1, ToTagList(tags));
    public void RecordMissingAfterResume(MetricsTagList tags) => _missingAfterResume.Add(1, ToTagList(tags));

    // --- NodeTranslation Export ---
    public void RecordNodeExportTreeCount(int count, MetricsTagList tags) => _nodeExportTreeCount.Record(count, ToTagList(tags));
    public void RecordNodeExportTreeDuration(double milliseconds, MetricsTagList tags) => _nodeExportTreeDuration.Record(milliseconds, ToTagList(tags));
    public void RecordNodeExportTreeError(MetricsTagList tags) => _nodeExportTreeErrors.Add(1, ToTagList(tags));

    // --- NodeTranslation Translate ---
    public void RecordNodeTranslateCount(MetricsTagList tags) => _nodeTranslateCount.Add(1, ToTagList(tags));
    public void RecordNodeTranslateMapHit(MetricsTagList tags) => _nodeTranslateMapHit.Add(1, ToTagList(tags));
    public void RecordNodeTranslateAutoSwapHit(MetricsTagList tags) => _nodeTranslateAutoSwapHit.Add(1, ToTagList(tags));
    public void RecordNodeTranslateExternal(MetricsTagList tags) => _nodeTranslateExternal.Add(1, ToTagList(tags));
    public void RecordNodeTranslateUnresolvable(MetricsTagList tags) => _nodeTranslateUnresolvable.Add(1, ToTagList(tags));

    // --- NodeTranslation Import: Replicate ---
    public void RecordNodeImportReplicateCount(MetricsTagList tags) => _nodeImportReplicateCount.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateAreaCount(MetricsTagList tags) => _nodeImportReplicateAreaCount.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateIterationCount(MetricsTagList tags) => _nodeImportReplicateIterationCount.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateDuration(double milliseconds, MetricsTagList tags) => _nodeImportReplicateDuration.Record(milliseconds, ToTagList(tags));
    public void RecordNodeImportReplicateError(MetricsTagList tags) => _nodeImportReplicateErrors.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateSkipped(MetricsTagList tags) => _nodeImportReplicateSkipped.Add(1, ToTagList(tags));
    public void IncrementNodeImportReplicateInFlight(MetricsTagList tags) => _nodeImportReplicateInFlight.Add(1, ToTagList(tags));
    public void DecrementNodeImportReplicateInFlight(MetricsTagList tags) => _nodeImportReplicateInFlight.Add(-1, ToTagList(tags));

    // --- NodeTranslation Import: PreCollect ---
    public void RecordNodeImportPreCollectCount(MetricsTagList tags) => _nodeImportPreCollectCount.Add(1, ToTagList(tags));
    public void RecordNodeImportPreCollectDuration(double milliseconds, MetricsTagList tags) => _nodeImportPreCollectDuration.Record(milliseconds, ToTagList(tags));
    public void RecordNodeImportPreCollectError(MetricsTagList tags) => _nodeImportPreCollectErrors.Add(1, ToTagList(tags));
    public void IncrementNodeImportPreCollectInFlight(MetricsTagList tags) => _nodeImportPreCollectInFlight.Add(1, ToTagList(tags));
    public void DecrementNodeImportPreCollectInFlight(MetricsTagList tags) => _nodeImportPreCollectInFlight.Add(-1, ToTagList(tags));

    // --- Teams Export ---
    public void RecordTeamExportCount(MetricsTagList tags) => _teamExportCount.Add(1, ToTagList(tags));
    public void RecordTeamExportDuration(double milliseconds, MetricsTagList tags) => _teamExportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordTeamExportError(MetricsTagList tags) => _teamExportErrors.Add(1, ToTagList(tags));
    public void IncrementTeamExportInFlight(MetricsTagList tags) => _teamExportInFlight.Add(1, ToTagList(tags));
    public void DecrementTeamExportInFlight(MetricsTagList tags) => _teamExportInFlight.Add(-1, ToTagList(tags));

    // --- Teams Import ---
    public void RecordTeamImportCount(MetricsTagList tags) => _teamImportCount.Add(1, ToTagList(tags));
    public void RecordTeamImportDuration(double milliseconds, MetricsTagList tags) => _teamImportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordTeamImportError(MetricsTagList tags) => _teamImportErrors.Add(1, ToTagList(tags));
    public void IncrementTeamImportInFlight(MetricsTagList tags) => _teamImportInFlight.Add(1, ToTagList(tags));
    public void DecrementTeamImportInFlight(MetricsTagList tags) => _teamImportInFlight.Add(-1, ToTagList(tags));
    public void RecordTeamImportMemberCount(MetricsTagList tags) => _teamImportMembersCount.Add(1, ToTagList(tags));
    public void RecordTeamImportMemberUnresolved(MetricsTagList tags) => _teamImportMembersUnresolved.Add(1, ToTagList(tags));
    public void RecordTeamImportIterationCount(MetricsTagList tags) => _teamImportIterationsCount.Add(1, ToTagList(tags));
    public void RecordTeamImportIterationUnresolvable(MetricsTagList tags) => _teamImportIterationsUnresolvable.Add(1, ToTagList(tags));
    public void RecordTeamImportCapacityCount(MetricsTagList tags) => _teamImportCapacityCount.Add(1, ToTagList(tags));
    public void RecordTeamImportExtensionDuration(double milliseconds, MetricsTagList tags) => _teamImportExtensionDuration.Record(milliseconds, ToTagList(tags));

    // --- Teams Validate ---
    public void RecordTeamValidateCount(MetricsTagList tags) => _teamValidateCount.Add(1, ToTagList(tags));
    public void RecordTeamValidateError(MetricsTagList tags) => _teamValidateErrors.Add(1, ToTagList(tags));

    // --- Identities Export ---
    public void RecordIdentityExportCount(MetricsTagList tags) => _identityExportCount.Add(1, ToTagList(tags));
    public void RecordIdentityExportDuration(double milliseconds, MetricsTagList tags) => _identityExportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordIdentityExportError(MetricsTagList tags) => _identityExportErrors.Add(1, ToTagList(tags));
    public void IncrementIdentityExportInFlight(MetricsTagList tags) => _identityExportInFlight.Add(1, ToTagList(tags));
    public void DecrementIdentityExportInFlight(MetricsTagList tags) => _identityExportInFlight.Add(-1, ToTagList(tags));

    // --- Identities Import ---
    public void RecordIdentityImportResolved(MetricsTagList tags) => _identityImportResolved.Add(1, ToTagList(tags));
    public void RecordIdentityImportUnresolved(MetricsTagList tags) => _identityImportUnresolved.Add(1, ToTagList(tags));
    public void RecordIdentityImportDuration(double milliseconds, MetricsTagList tags) => _identityImportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordIdentityImportError(MetricsTagList tags) => _identityImportErrors.Add(1, ToTagList(tags));

    // --- Identities Validate ---
    public void RecordIdentityValidateCount(MetricsTagList tags) => _identityValidateCount.Add(1, ToTagList(tags));
    public void RecordIdentityValidateError(MetricsTagList tags) => _identityValidateErrors.Add(1, ToTagList(tags));

    // --- Prepare ---
    public void RecordPrepareWorkItemsResolved(int count, MetricsTagList tags) => _prepareWorkItemsResolved.Add(count, ToTagList(tags));
    public void RecordPrepareWorkItemsUnresolved(int count, MetricsTagList tags) => _prepareWorkItemsUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareWorkItemsDuration(double milliseconds, MetricsTagList tags) => _prepareWorkItemsDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareWorkItemsError(MetricsTagList tags) => _prepareWorkItemsErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareWorkItemsInFlight(MetricsTagList tags) => _prepareWorkItemsInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareWorkItemsInFlight(MetricsTagList tags) => _prepareWorkItemsInFlight.Add(-1, ToTagList(tags));
    public void RecordPrepareIdentitiesResolved(int count, MetricsTagList tags) => _prepareIdentitiesResolved.Add(count, ToTagList(tags));
    public void RecordPrepareIdentitiesUnresolved(int count, MetricsTagList tags) => _prepareIdentitiesUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareIdentitiesDuration(double milliseconds, MetricsTagList tags) => _prepareIdentitiesDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareIdentitiesError(MetricsTagList tags) => _prepareIdentitiesErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareIdentitiesInFlight(MetricsTagList tags) => _prepareIdentitiesInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareIdentitiesInFlight(MetricsTagList tags) => _prepareIdentitiesInFlight.Add(-1, ToTagList(tags));
    public void RecordPrepareNodesResolved(int count, MetricsTagList tags) => _prepareNodesResolved.Add(count, ToTagList(tags));
    public void RecordPrepareNodesUnresolved(int count, MetricsTagList tags) => _prepareNodesUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareNodesDuration(double milliseconds, MetricsTagList tags) => _prepareNodesDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareNodesError(MetricsTagList tags) => _prepareNodesErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareNodesInFlight(MetricsTagList tags) => _prepareNodesInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareNodesInFlight(MetricsTagList tags) => _prepareNodesInFlight.Add(-1, ToTagList(tags));
    public void RecordPrepareTeamsResolved(int count, MetricsTagList tags) => _prepareTeamsResolved.Add(count, ToTagList(tags));
    public void RecordPrepareTeamsUnresolved(int count, MetricsTagList tags) => _prepareTeamsUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareTeamsDuration(double milliseconds, MetricsTagList tags) => _prepareTeamsDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareTeamsError(MetricsTagList tags) => _prepareTeamsErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareTeamsInFlight(MetricsTagList tags) => _prepareTeamsInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareTeamsInFlight(MetricsTagList tags) => _prepareTeamsInFlight.Add(-1, ToTagList(tags));

    // --- Package Config ---
    public void RecordConfigWriteCompleted(MetricsTagList tags) => _configWriteCount.Add(1, ToTagList(tags));
    public void RecordConfigWriteError(MetricsTagList tags) => _configWriteErrors.Add(1, ToTagList(tags));
    public void RecordConfigReadCompleted(MetricsTagList tags) => _configReadCount.Add(1, ToTagList(tags));
    public void RecordConfigReadError(MetricsTagList tags) => _configReadErrors.Add(1, ToTagList(tags));
    public void RecordConfigReadFallback(MetricsTagList tags) => _configReadFallbacks.Add(1, ToTagList(tags));

    public void Dispose() => _meter.Dispose();
}

