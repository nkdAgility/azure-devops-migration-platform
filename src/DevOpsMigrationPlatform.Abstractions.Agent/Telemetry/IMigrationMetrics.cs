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
}
