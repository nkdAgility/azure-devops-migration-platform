// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Unified recording contract for all migration OTel metric instruments.
/// All methods accept a pre-built <see cref="MetricsTagList"/> carrying the mandatory
/// <c>job.id</c>, <c>operation</c>, and <c>module</c> dimension tags.
/// </summary>
public interface IMigrationMetrics
{
    // --- Execution ---
    void RecordWorkItemAttempted(MetricsTagList tags);
    void RecordWorkItemCompleted(MetricsTagList tags);
    void RecordWorkItemFailed(MetricsTagList tags);
    void RecordWorkItemRetried(MetricsTagList tags);
    void RecordWorkItemDuration(double milliseconds, MetricsTagList tags);

    // --- Payload / Complexity ---
    void RecordFieldCount(int count, MetricsTagList tags);
    void RecordAttachmentCount(int count, MetricsTagList tags);
    void RecordAttachmentDownloadDuration(double milliseconds, MetricsTagList tags);
    void RecordAttachmentDownloadBytes(long bytes, MetricsTagList tags);
    void RecordLinkCount(int count, MetricsTagList tags);
    void RecordRevisionCount(int count, MetricsTagList tags);
    void RecordPayloadBytes(long bytes, MetricsTagList tags);

    // --- Correctness (Tier 3 only) ---
    void RecordRevisionSourceCount(int count, MetricsTagList tags);
    void RecordRevisionTargetCount(int count, MetricsTagList tags);
    void RecordRevisionDelta(int delta, MetricsTagList tags);
    void RecordRevisionsMissing(MetricsTagList tags);
    void RecordRevisionOrderError(MetricsTagList tags);
    void RecordBrokenLink(MetricsTagList tags);
    void RecordMissingWorkItem(MetricsTagList tags);

    // --- In-Flight ---
    void IncrementInFlight(MetricsTagList tags);
    void DecrementInFlight(MetricsTagList tags);

    // --- FieldTransform ---
    void RecordFieldTransformApplied(MetricsTagList tags);
    void RecordFieldTransformDuration(double milliseconds, MetricsTagList tags);
    void RecordFieldTransformError(MetricsTagList tags);
    void IncrementFieldTransformInFlight(MetricsTagList tags);
    void DecrementFieldTransformInFlight(MetricsTagList tags);
    void RecordFieldTransformFieldsModified(int count, MetricsTagList tags);

    // --- Idempotency (deferred — counters registered, not yet incremented in production) ---
    void RecordDuplicated(MetricsTagList tags);
    void RecordChangedOnRerun(MetricsTagList tags);
    void RecordReprocessedAfterResume(MetricsTagList tags);
    void RecordDuplicatedAfterResume(MetricsTagList tags);
    void RecordMissingAfterResume(MetricsTagList tags);

    // --- NodeTranslation Export ---
    void RecordNodeExportTreeCount(int count, MetricsTagList tags);
    void RecordNodeExportTreeDuration(double milliseconds, MetricsTagList tags);
    void RecordNodeExportTreeError(MetricsTagList tags);

    // --- NodeTranslation Translate ---
    void RecordNodeTranslateCount(MetricsTagList tags);
    void RecordNodeTranslateMapHit(MetricsTagList tags);
    void RecordNodeTranslateAutoSwapHit(MetricsTagList tags);
    void RecordNodeTranslateExternal(MetricsTagList tags);
    void RecordNodeTranslateUnresolvable(MetricsTagList tags);

    // --- NodeTranslation Import: Replicate ---
    void RecordNodeImportReplicateCount(MetricsTagList tags);
    void RecordNodeImportReplicateAreaCount(MetricsTagList tags);
    void RecordNodeImportReplicateIterationCount(MetricsTagList tags);
    void RecordNodeImportReplicateDuration(double milliseconds, MetricsTagList tags);
    void RecordNodeImportReplicateError(MetricsTagList tags);
    void RecordNodeImportReplicateSkipped(MetricsTagList tags);
    void IncrementNodeImportReplicateInFlight(MetricsTagList tags);
    void DecrementNodeImportReplicateInFlight(MetricsTagList tags);

    // --- NodeTranslation Import: PreCollect ---
    void RecordNodeImportPreCollectCount(MetricsTagList tags);
    void RecordNodeImportPreCollectDuration(double milliseconds, MetricsTagList tags);
    void RecordNodeImportPreCollectError(MetricsTagList tags);
    void IncrementNodeImportPreCollectInFlight(MetricsTagList tags);
    void DecrementNodeImportPreCollectInFlight(MetricsTagList tags);

    // --- Teams Export ---
    void RecordTeamExportCount(MetricsTagList tags);
    void RecordTeamExportDuration(double milliseconds, MetricsTagList tags);
    void RecordTeamExportError(MetricsTagList tags);
    void IncrementTeamExportInFlight(MetricsTagList tags);
    void DecrementTeamExportInFlight(MetricsTagList tags);

    // --- Teams Import ---
    void RecordTeamImportCount(MetricsTagList tags);
    void RecordTeamImportDuration(double milliseconds, MetricsTagList tags);
    void RecordTeamImportError(MetricsTagList tags);
    void IncrementTeamImportInFlight(MetricsTagList tags);
    void DecrementTeamImportInFlight(MetricsTagList tags);
    void RecordTeamImportMemberCount(MetricsTagList tags);
    void RecordTeamImportMemberUnresolved(MetricsTagList tags);
    void RecordTeamImportIterationCount(MetricsTagList tags);
    void RecordTeamImportIterationUnresolvable(MetricsTagList tags);
    void RecordTeamImportCapacityCount(MetricsTagList tags);
    void RecordTeamImportExtensionDuration(double milliseconds, MetricsTagList tags);

    // --- Teams Validate ---
    void RecordTeamValidateCount(MetricsTagList tags);
    void RecordTeamValidateError(MetricsTagList tags);

    // --- Identities Export ---
    void RecordIdentityExportCount(MetricsTagList tags);
    void RecordIdentityExportDuration(double milliseconds, MetricsTagList tags);
    void RecordIdentityExportError(MetricsTagList tags);
    void IncrementIdentityExportInFlight(MetricsTagList tags);
    void DecrementIdentityExportInFlight(MetricsTagList tags);

    // --- Identities Import ---
    void RecordIdentityImportResolved(MetricsTagList tags);
    void RecordIdentityImportUnresolved(MetricsTagList tags);
    void RecordIdentityImportDuration(double milliseconds, MetricsTagList tags);
    void RecordIdentityImportError(MetricsTagList tags);

    // --- Identities Validate ---
    void RecordIdentityValidateCount(MetricsTagList tags);
    void RecordIdentityValidateError(MetricsTagList tags);

    // --- Prepare ---
    void RecordPrepareWorkItemsResolved(int count, MetricsTagList tags);
    void RecordPrepareWorkItemsUnresolved(int count, MetricsTagList tags);
    void RecordPrepareWorkItemsDuration(double milliseconds, MetricsTagList tags);
    void RecordPrepareWorkItemsError(MetricsTagList tags);
    void IncrementPrepareWorkItemsInFlight(MetricsTagList tags);
    void DecrementPrepareWorkItemsInFlight(MetricsTagList tags);
    void RecordPrepareIdentitiesResolved(int count, MetricsTagList tags);
    void RecordPrepareIdentitiesUnresolved(int count, MetricsTagList tags);
    void RecordPrepareIdentitiesDuration(double milliseconds, MetricsTagList tags);
    void RecordPrepareIdentitiesError(MetricsTagList tags);
    void IncrementPrepareIdentitiesInFlight(MetricsTagList tags);
    void DecrementPrepareIdentitiesInFlight(MetricsTagList tags);
    void RecordPrepareNodesResolved(int count, MetricsTagList tags);
    void RecordPrepareNodesUnresolved(int count, MetricsTagList tags);
    void RecordPrepareNodesDuration(double milliseconds, MetricsTagList tags);
    void RecordPrepareNodesError(MetricsTagList tags);
    void IncrementPrepareNodesInFlight(MetricsTagList tags);
    void DecrementPrepareNodesInFlight(MetricsTagList tags);
    void RecordPrepareTeamsResolved(int count, MetricsTagList tags);
    void RecordPrepareTeamsUnresolved(int count, MetricsTagList tags);
    void RecordPrepareTeamsDuration(double milliseconds, MetricsTagList tags);
    void RecordPrepareTeamsError(MetricsTagList tags);
    void IncrementPrepareTeamsInFlight(MetricsTagList tags);
    void DecrementPrepareTeamsInFlight(MetricsTagList tags);

    // --- Package Config (feature 025-agent-config-package) ---
    void RecordConfigWriteCompleted(MetricsTagList tags);
    void RecordConfigWriteError(MetricsTagList tags);
    void RecordConfigReadCompleted(MetricsTagList tags);
    void RecordConfigReadError(MetricsTagList tags);
    void RecordConfigReadFallback(MetricsTagList tags);
}
