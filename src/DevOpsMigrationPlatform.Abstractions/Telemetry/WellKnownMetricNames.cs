namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// OpenTelemetry instrument name constants using the <c>migration.</c> dot-separated convention.
/// These names are the public contract — renaming is a breaking change requiring a version increment.
/// </summary>
public static class WellKnownMetricNames
{
    // --- Execution ---
    public const string WorkItemsAttempted = "migration.workitems.attempted";
    public const string WorkItemsCompleted = "migration.workitems.completed";
    public const string WorkItemsFailed = "migration.workitems.failed";
    public const string WorkItemsRetried = "migration.workitems.retried";
    public const string WorkItemDurationMs = "migration.workitem.duration.ms";

    // --- Payload / Complexity ---
    public const string FieldCount = "migration.workitem.fields.count";
    public const string AttachmentCount = "migration.workitem.attachments.count";
    public const string AttachmentDownloadDurationMs = "migration.attachment.download.duration.ms";
    public const string AttachmentDownloadBytes = "migration.attachment.download.bytes";
    public const string LinkCount = "migration.workitem.links.count";
    public const string RevisionCount = "migration.workitem.revisions.count";
    public const string PayloadBytes = "migration.workitem.payload.bytes";

    // --- Correctness (Tier 3 post-flight only) ---
    public const string RevisionSourceCount = "migration.workitem.revisions.source.count";
    public const string RevisionTargetCount = "migration.workitem.revisions.target.count";
    public const string RevisionDelta = "migration.workitem.revisions.delta";
    public const string RevisionsMissing = "migration.workitems.revisions.missing";
    public const string RevisionOrderErrors = "migration.workitems.revision_order_errors";
    public const string BrokenLinks = "migration.workitems.broken_links";
    public const string MissingWorkItems = "migration.workitems.missing";

    // --- In-Flight ---
    public const string WorkItemsInFlight = "migration.workitems.in_flight";
    public const string QueueDepth = "migration.queue.workitems.depth";

    // --- FieldTransform ---
    public const string FieldTransformApplyCount = "migration.fieldtransform.apply.count";
    public const string FieldTransformApplyDurationMs = "migration.fieldtransform.apply.duration_ms";
    public const string FieldTransformApplyErrors = "migration.fieldtransform.apply.errors";
    public const string FieldTransformApplyInFlight = "migration.fieldtransform.apply.in_flight";
    public const string FieldTransformApplyFieldsModified = "migration.fieldtransform.apply.fields_modified";

    // --- Idempotency (deferred — instruments registered, not yet incremented) ---
    public const string Duplicated = "migration.workitems.duplicated";
    public const string ChangedOnRerun = "migration.workitems.changed_on_rerun";
    public const string ReprocessedAfterResume = "migration.workitems.reprocessed_after_resume";
    public const string DuplicatedAfterResume = "migration.workitems.duplicated_after_resume";
    public const string MissingAfterResume = "migration.workitems.missing_after_resume";
}