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

    // --- NodeTranslation ---
    public const string NodeTranslateCount = "migration.nodes.import.translate.count";
    public const string NodeTranslateMapHit = "migration.nodes.import.translate.map_hit";
    public const string NodeTranslateAutoSwapHit = "migration.nodes.import.translate.autoswap_hit";
    public const string NodeTranslateExternal = "migration.nodes.import.translate.external";
    public const string NodeTranslateUnresolvable = "migration.nodes.import.translate.unresolvable";
    public const string NodeExportDiscoverCount = "migration.nodes.export.discover.count";
    public const string NodeExportTreeCount = "migration.nodes.export.tree.count";
    public const string NodeExportTreeDurationMs = "migration.nodes.export.tree.duration_ms";
    public const string NodeExportTreeErrors = "migration.nodes.export.tree.errors";
    public const string NodeImportPreCollectCount = "migration.nodes.import.precollect.count";
    public const string NodeImportPreCollectDurationMs = "migration.nodes.import.precollect.duration_ms";
    public const string NodeImportPreCollectErrors = "migration.nodes.import.precollect.errors";
    public const string NodeImportPreCollectInFlight = "migration.nodes.import.precollect.in_flight";
    public const string NodeImportReplicateCount = "migration.nodes.import.replicate.count";
    public const string NodeImportReplicateAreaCount = "migration.nodes.import.replicate.area.count";
    public const string NodeImportReplicateIterationCount = "migration.nodes.import.replicate.iteration.count";
    public const string NodeImportReplicateDurationMs = "migration.nodes.import.replicate.duration_ms";
    public const string NodeImportReplicateErrors = "migration.nodes.import.replicate.errors";
    public const string NodeImportReplicateSkipped = "migration.nodes.import.replicate.skipped";
    public const string NodeImportReplicateInFlight = "migration.nodes.import.replicate.in_flight";
    public const string NodeValidateDurationMs = "migration.nodes.validate.duration_ms";
    public const string NodeValidateUnmappedPaths = "migration.nodes.validate.unmapped_paths";
    public const string NodeValidateExternalPaths = "migration.nodes.validate.external_paths";
    public const string NodeValidateMalformedTargets = "migration.nodes.validate.malformed_targets";

    // --- Teams Export ---
    public const string TeamsExportCount = "migration.teams.export.count";
    public const string TeamsExportDurationMs = "migration.teams.export.duration_ms";
    public const string TeamsExportErrors = "migration.teams.export.errors";
    public const string TeamsExportInFlight = "migration.teams.export.in_flight";

    // --- Teams Import ---
    public const string TeamsImportCount = "migration.teams.import.count";
    public const string TeamsImportDurationMs = "migration.teams.import.duration_ms";
    public const string TeamsImportErrors = "migration.teams.import.errors";
    public const string TeamsImportInFlight = "migration.teams.import.in_flight";
    public const string TeamsImportMembersCount = "migration.teams.import.members.count";
    public const string TeamsImportMembersUnresolved = "migration.teams.import.members.unresolved";
    public const string TeamsImportIterationsCount = "migration.teams.import.iterations.count";
    public const string TeamsImportIterationsUnresolvable = "migration.teams.import.iterations.unresolvable";
    public const string TeamsImportCapacityCount = "migration.teams.import.capacity.count";
    public const string TeamsImportExtensionDurationMs = "migration.teams.import.extension.duration_ms";

    // --- Teams Validate ---
    public const string TeamsValidateCount = "migration.teams.validate.count";
    public const string TeamsValidateErrors = "migration.teams.validate.errors";

    // --- Identities Export ---
    public const string IdentitiesExportCount = "migration.identities.export.count";
    public const string IdentitiesExportDurationMs = "migration.identities.export.duration_ms";
    public const string IdentitiesExportErrors = "migration.identities.export.errors";
    public const string IdentitiesExportInFlight = "migration.identities.export.in_flight";

    // --- Identities Import ---
    public const string IdentitiesImportResolved = "migration.identities.import.resolved";
    public const string IdentitiesImportUnresolved = "migration.identities.import.unresolved";
    public const string IdentitiesImportDurationMs = "migration.identities.import.duration_ms";
    public const string IdentitiesImportErrors = "migration.identities.import.errors";

    // --- Identities Validate ---
    public const string IdentitiesValidateCount = "migration.identities.validate.count";
    public const string IdentitiesValidateErrors = "migration.identities.validate.errors";
}