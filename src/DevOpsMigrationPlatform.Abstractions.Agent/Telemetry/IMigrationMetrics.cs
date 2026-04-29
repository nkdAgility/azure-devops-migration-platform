using System.Diagnostics;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Unified recording contract for all migration OTel metric instruments.
/// All methods accept a pre-built <see cref="TagList"/> carrying the mandatory
/// <c>job.id</c>, <c>operation</c>, and <c>module</c> dimension tags.
/// </summary>
public interface IMigrationMetrics
{
    // --- Execution ---
    void RecordWorkItemAttempted(in TagList tags);
    void RecordWorkItemCompleted(in TagList tags);
    void RecordWorkItemFailed(in TagList tags);
    void RecordWorkItemRetried(in TagList tags);
    void RecordWorkItemDuration(double milliseconds, in TagList tags);

    // --- Payload / Complexity ---
    void RecordFieldCount(int count, in TagList tags);
    void RecordAttachmentCount(int count, in TagList tags);
    void RecordAttachmentDownloadDuration(double milliseconds, in TagList tags);
    void RecordAttachmentDownloadBytes(long bytes, in TagList tags);
    void RecordLinkCount(int count, in TagList tags);
    void RecordRevisionCount(int count, in TagList tags);
    void RecordPayloadBytes(long bytes, in TagList tags);

    // --- Correctness (Tier 3 only) ---
    void RecordRevisionSourceCount(int count, in TagList tags);
    void RecordRevisionTargetCount(int count, in TagList tags);
    void RecordRevisionDelta(int delta, in TagList tags);
    void RecordRevisionsMissing(in TagList tags);
    void RecordRevisionOrderError(in TagList tags);
    void RecordBrokenLink(in TagList tags);
    void RecordMissingWorkItem(in TagList tags);

    // --- In-Flight ---
    void IncrementInFlight(in TagList tags);
    void DecrementInFlight(in TagList tags);

    // --- FieldTransform ---
    void RecordFieldTransformApplied(in TagList tags);
    void RecordFieldTransformDuration(double milliseconds, in TagList tags);
    void RecordFieldTransformError(in TagList tags);
    void IncrementFieldTransformInFlight(in TagList tags);
    void DecrementFieldTransformInFlight(in TagList tags);
    void RecordFieldTransformFieldsModified(int count, in TagList tags);

    // --- Idempotency (deferred — counters registered, not yet incremented in production) ---
    void RecordDuplicated(in TagList tags);
    void RecordChangedOnRerun(in TagList tags);
    void RecordReprocessedAfterResume(in TagList tags);
    void RecordDuplicatedAfterResume(in TagList tags);
    void RecordMissingAfterResume(in TagList tags);

    // --- NodeTranslation Export ---
    void RecordNodeExportTreeCount(int count, in TagList tags);
    void RecordNodeExportTreeDuration(double milliseconds, in TagList tags);
    void RecordNodeExportTreeError(in TagList tags);

    // --- NodeTranslation Translate ---
    void RecordNodeTranslateCount(in TagList tags);
    void RecordNodeTranslateMapHit(in TagList tags);
    void RecordNodeTranslateAutoSwapHit(in TagList tags);
    void RecordNodeTranslateExternal(in TagList tags);
    void RecordNodeTranslateUnresolvable(in TagList tags);

    // --- NodeTranslation Import: Replicate ---
    void RecordNodeImportReplicateCount(in TagList tags);
    void RecordNodeImportReplicateAreaCount(in TagList tags);
    void RecordNodeImportReplicateIterationCount(in TagList tags);
    void RecordNodeImportReplicateDuration(double milliseconds, in TagList tags);
    void RecordNodeImportReplicateError(in TagList tags);
    void RecordNodeImportReplicateSkipped(in TagList tags);
    void IncrementNodeImportReplicateInFlight(in TagList tags);
    void DecrementNodeImportReplicateInFlight(in TagList tags);

    // --- NodeTranslation Import: PreCollect ---
    void RecordNodeImportPreCollectCount(in TagList tags);
    void RecordNodeImportPreCollectDuration(double milliseconds, in TagList tags);
    void RecordNodeImportPreCollectError(in TagList tags);
    void IncrementNodeImportPreCollectInFlight(in TagList tags);
    void DecrementNodeImportPreCollectInFlight(in TagList tags);

    // --- Teams Export ---
    void RecordTeamExportCount(in TagList tags);
    void RecordTeamExportDuration(double milliseconds, in TagList tags);
    void RecordTeamExportError(in TagList tags);
    void IncrementTeamExportInFlight(in TagList tags);
    void DecrementTeamExportInFlight(in TagList tags);

    // --- Teams Import ---
    void RecordTeamImportCount(in TagList tags);
    void RecordTeamImportDuration(double milliseconds, in TagList tags);
    void RecordTeamImportError(in TagList tags);
    void IncrementTeamImportInFlight(in TagList tags);
    void DecrementTeamImportInFlight(in TagList tags);
    void RecordTeamImportMemberCount(in TagList tags);
    void RecordTeamImportMemberUnresolved(in TagList tags);
    void RecordTeamImportIterationCount(in TagList tags);
    void RecordTeamImportIterationUnresolvable(in TagList tags);
    void RecordTeamImportCapacityCount(in TagList tags);
    void RecordTeamImportExtensionDuration(double milliseconds, in TagList tags);

    // --- Teams Validate ---
    void RecordTeamValidateCount(in TagList tags);
    void RecordTeamValidateError(in TagList tags);

    // --- Identities Export ---
    void RecordIdentityExportCount(in TagList tags);
    void RecordIdentityExportDuration(double milliseconds, in TagList tags);
    void RecordIdentityExportError(in TagList tags);
    void IncrementIdentityExportInFlight(in TagList tags);
    void DecrementIdentityExportInFlight(in TagList tags);

    // --- Identities Import ---
    void RecordIdentityImportResolved(in TagList tags);
    void RecordIdentityImportUnresolved(in TagList tags);
    void RecordIdentityImportDuration(double milliseconds, in TagList tags);
    void RecordIdentityImportError(in TagList tags);

    // --- Identities Validate ---
    void RecordIdentityValidateCount(in TagList tags);
    void RecordIdentityValidateError(in TagList tags);

    // --- Package Config (feature 025-agent-config-package) ---
    void RecordConfigWriteCompleted(in TagList tags);
    void RecordConfigWriteError(in TagList tags);
    void RecordConfigReadCompleted(in TagList tags);
    void RecordConfigReadError(in TagList tags);
    void RecordConfigReadFallback(in TagList tags);
}
