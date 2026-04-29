using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;

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

        // Package Config
        _configWriteCount = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigWriteCount, unit: "{operation}");
        _configWriteErrors = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigWriteErrors, unit: "{error}");
        _configReadCount = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigReadCount, unit: "{operation}");
        _configReadErrors = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigReadErrors, unit: "{error}");
        _configReadFallbacks = _meter.CreateCounter<long>(WellKnownMetricNames.ConfigReadFallbacks, unit: "{fallback}");
    }
    public void RecordWorkItemAttempted(in TagList tags) => _attempted.Add(1, tags);
    public void RecordWorkItemCompleted(in TagList tags) => _completed.Add(1, tags);
    public void RecordWorkItemFailed(in TagList tags) => _failed.Add(1, tags);
    public void RecordWorkItemRetried(in TagList tags) => _retried.Add(1, tags);
    public void RecordWorkItemDuration(double milliseconds, in TagList tags) => _duration.Record(milliseconds, tags);

    // --- Payload ---
    public void RecordFieldCount(int count, in TagList tags) => _fieldCount.Record(count, tags);
    public void RecordAttachmentCount(int count, in TagList tags) => _attachmentCount.Record(count, tags);
    public void RecordAttachmentDownloadDuration(double milliseconds, in TagList tags) => _attachmentDownloadDuration.Record(milliseconds, tags);
    public void RecordAttachmentDownloadBytes(long bytes, in TagList tags) => _attachmentDownloadBytes.Record(bytes, tags);
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

    // --- FieldTransform ---
    public void RecordFieldTransformApplied(in TagList tags) => _fieldTransformApplied.Add(1, tags);
    public void RecordFieldTransformDuration(double milliseconds, in TagList tags) => _fieldTransformDuration.Record(milliseconds, tags);
    public void RecordFieldTransformError(in TagList tags) => _fieldTransformErrors.Add(1, tags);
    public void IncrementFieldTransformInFlight(in TagList tags) => _fieldTransformInFlight.Add(1, tags);
    public void DecrementFieldTransformInFlight(in TagList tags) => _fieldTransformInFlight.Add(-1, tags);
    public void RecordFieldTransformFieldsModified(int count, in TagList tags) => _fieldTransformFieldsModified.Record(count, tags);

    // --- Idempotency ---
    public void RecordDuplicated(in TagList tags) => _duplicated.Add(1, tags);
    public void RecordChangedOnRerun(in TagList tags) => _changedOnRerun.Add(1, tags);
    public void RecordReprocessedAfterResume(in TagList tags) => _reprocessedAfterResume.Add(1, tags);
    public void RecordDuplicatedAfterResume(in TagList tags) => _duplicatedAfterResume.Add(1, tags);
    public void RecordMissingAfterResume(in TagList tags) => _missingAfterResume.Add(1, tags);

    // --- NodeTranslation Export ---
    public void RecordNodeExportTreeCount(int count, in TagList tags) => _nodeExportTreeCount.Record(count, tags);
    public void RecordNodeExportTreeDuration(double milliseconds, in TagList tags) => _nodeExportTreeDuration.Record(milliseconds, tags);
    public void RecordNodeExportTreeError(in TagList tags) => _nodeExportTreeErrors.Add(1, tags);

    // --- NodeTranslation Translate ---
    public void RecordNodeTranslateCount(in TagList tags) => _nodeTranslateCount.Add(1, tags);
    public void RecordNodeTranslateMapHit(in TagList tags) => _nodeTranslateMapHit.Add(1, tags);
    public void RecordNodeTranslateAutoSwapHit(in TagList tags) => _nodeTranslateAutoSwapHit.Add(1, tags);
    public void RecordNodeTranslateExternal(in TagList tags) => _nodeTranslateExternal.Add(1, tags);
    public void RecordNodeTranslateUnresolvable(in TagList tags) => _nodeTranslateUnresolvable.Add(1, tags);

    // --- NodeTranslation Import: Replicate ---
    public void RecordNodeImportReplicateCount(in TagList tags) => _nodeImportReplicateCount.Add(1, tags);
    public void RecordNodeImportReplicateAreaCount(in TagList tags) => _nodeImportReplicateAreaCount.Add(1, tags);
    public void RecordNodeImportReplicateIterationCount(in TagList tags) => _nodeImportReplicateIterationCount.Add(1, tags);
    public void RecordNodeImportReplicateDuration(double milliseconds, in TagList tags) => _nodeImportReplicateDuration.Record(milliseconds, tags);
    public void RecordNodeImportReplicateError(in TagList tags) => _nodeImportReplicateErrors.Add(1, tags);
    public void RecordNodeImportReplicateSkipped(in TagList tags) => _nodeImportReplicateSkipped.Add(1, tags);
    public void IncrementNodeImportReplicateInFlight(in TagList tags) => _nodeImportReplicateInFlight.Add(1, tags);
    public void DecrementNodeImportReplicateInFlight(in TagList tags) => _nodeImportReplicateInFlight.Add(-1, tags);

    // --- NodeTranslation Import: PreCollect ---
    public void RecordNodeImportPreCollectCount(in TagList tags) => _nodeImportPreCollectCount.Add(1, tags);
    public void RecordNodeImportPreCollectDuration(double milliseconds, in TagList tags) => _nodeImportPreCollectDuration.Record(milliseconds, tags);
    public void RecordNodeImportPreCollectError(in TagList tags) => _nodeImportPreCollectErrors.Add(1, tags);
    public void IncrementNodeImportPreCollectInFlight(in TagList tags) => _nodeImportPreCollectInFlight.Add(1, tags);
    public void DecrementNodeImportPreCollectInFlight(in TagList tags) => _nodeImportPreCollectInFlight.Add(-1, tags);

    // --- Teams Export ---
    public void RecordTeamExportCount(in TagList tags) => _teamExportCount.Add(1, tags);
    public void RecordTeamExportDuration(double milliseconds, in TagList tags) => _teamExportDuration.Record(milliseconds, tags);
    public void RecordTeamExportError(in TagList tags) => _teamExportErrors.Add(1, tags);
    public void IncrementTeamExportInFlight(in TagList tags) => _teamExportInFlight.Add(1, tags);
    public void DecrementTeamExportInFlight(in TagList tags) => _teamExportInFlight.Add(-1, tags);

    // --- Teams Import ---
    public void RecordTeamImportCount(in TagList tags) => _teamImportCount.Add(1, tags);
    public void RecordTeamImportDuration(double milliseconds, in TagList tags) => _teamImportDuration.Record(milliseconds, tags);
    public void RecordTeamImportError(in TagList tags) => _teamImportErrors.Add(1, tags);
    public void IncrementTeamImportInFlight(in TagList tags) => _teamImportInFlight.Add(1, tags);
    public void DecrementTeamImportInFlight(in TagList tags) => _teamImportInFlight.Add(-1, tags);
    public void RecordTeamImportMemberCount(in TagList tags) => _teamImportMembersCount.Add(1, tags);
    public void RecordTeamImportMemberUnresolved(in TagList tags) => _teamImportMembersUnresolved.Add(1, tags);
    public void RecordTeamImportIterationCount(in TagList tags) => _teamImportIterationsCount.Add(1, tags);
    public void RecordTeamImportIterationUnresolvable(in TagList tags) => _teamImportIterationsUnresolvable.Add(1, tags);
    public void RecordTeamImportCapacityCount(in TagList tags) => _teamImportCapacityCount.Add(1, tags);
    public void RecordTeamImportExtensionDuration(double milliseconds, in TagList tags) => _teamImportExtensionDuration.Record(milliseconds, tags);

    // --- Teams Validate ---
    public void RecordTeamValidateCount(in TagList tags) => _teamValidateCount.Add(1, tags);
    public void RecordTeamValidateError(in TagList tags) => _teamValidateErrors.Add(1, tags);

    // --- Identities Export ---
    public void RecordIdentityExportCount(in TagList tags) => _identityExportCount.Add(1, tags);
    public void RecordIdentityExportDuration(double milliseconds, in TagList tags) => _identityExportDuration.Record(milliseconds, tags);
    public void RecordIdentityExportError(in TagList tags) => _identityExportErrors.Add(1, tags);
    public void IncrementIdentityExportInFlight(in TagList tags) => _identityExportInFlight.Add(1, tags);
    public void DecrementIdentityExportInFlight(in TagList tags) => _identityExportInFlight.Add(-1, tags);

    // --- Identities Import ---
    public void RecordIdentityImportResolved(in TagList tags) => _identityImportResolved.Add(1, tags);
    public void RecordIdentityImportUnresolved(in TagList tags) => _identityImportUnresolved.Add(1, tags);
    public void RecordIdentityImportDuration(double milliseconds, in TagList tags) => _identityImportDuration.Record(milliseconds, tags);
    public void RecordIdentityImportError(in TagList tags) => _identityImportErrors.Add(1, tags);

    // --- Identities Validate ---
    public void RecordIdentityValidateCount(in TagList tags) => _identityValidateCount.Add(1, tags);
    public void RecordIdentityValidateError(in TagList tags) => _identityValidateErrors.Add(1, tags);

    // --- Package Config ---
    public void RecordConfigWriteCompleted(in TagList tags) => _configWriteCount.Add(1, tags);
    public void RecordConfigWriteError(in TagList tags) => _configWriteErrors.Add(1, tags);
    public void RecordConfigReadCompleted(in TagList tags) => _configReadCount.Add(1, tags);
    public void RecordConfigReadError(in TagList tags) => _configReadErrors.Add(1, tags);
    public void RecordConfigReadFallback(in TagList tags) => _configReadFallbacks.Add(1, tags);

    public void Dispose() => _meter.Dispose();
}
