// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// OpenTelemetry instrument name constants for the <c>DevOpsMigrationPlatform.Agent</c> meter.
/// All instruments follow the <c>platform.&lt;domain&gt;.&lt;phase&gt;.&lt;measure&gt;</c> naming scheme.
/// These names are the public contract — renaming is a breaking change requiring a version increment.
/// </summary>
public static class WellKnownAgentMetricNames
{
    // --- Organisation Inventory ---
    public const string OrganisationsQueued = "platform.organisations.inventory.queued";
    public const string OrganisationsCompleted = "platform.organisations.inventory.completed";
    public const string OrganisationsFailed = "platform.organisations.inventory.failed";
    public const string OrganisationDurationMs = "platform.organisations.inventory.duration_ms";
    public const string OrganisationProjectCount = "platform.organisations.inventory.project_count";

    // --- Project Inventory ---
    public const string ProjectsQueued = "platform.projects.inventory.queued";
    public const string ProjectsCompleted = "platform.projects.inventory.completed";
    public const string ProjectsFailed = "platform.projects.inventory.failed";
    public const string ProjectDurationMs = "platform.projects.inventory.duration_ms";

    // --- WorkItems Inventory ---
    public const string InventoryWorkItems = "platform.workitems.inventory.count";
    public const string InventoryWorkItemsDurationMs = "platform.workitems.inventory.duration_ms";
    public const string InventoryWorkItemsErrors = "platform.workitems.inventory.errors";
    public const string InventoryWorkItemsInFlight = "platform.workitems.inventory.in_flight";
    public const string InventoryRevisions = "platform.workitems.inventory.revisions";

    // --- Identities Inventory ---
    public const string InventoryIdentities = "platform.identities.inventory.count";
    public const string InventoryIdentitiesDurationMs = "platform.identities.inventory.duration_ms";
    public const string InventoryIdentitiesErrors = "platform.identities.inventory.errors";
    public const string InventoryIdentitiesInFlight = "platform.identities.inventory.in_flight";

    // --- Nodes Inventory ---
    public const string InventoryNodes = "platform.nodes.inventory.count";
    public const string InventoryNodesDurationMs = "platform.nodes.inventory.duration_ms";
    public const string InventoryNodesErrors = "platform.nodes.inventory.errors";
    public const string InventoryNodesInFlight = "platform.nodes.inventory.in_flight";

    // --- Teams Inventory ---
    public const string InventoryTeams = "platform.teams.inventory.count";
    public const string InventoryTeamsDurationMs = "platform.teams.inventory.duration_ms";
    public const string InventoryTeamsErrors = "platform.teams.inventory.errors";
    public const string InventoryTeamsInFlight = "platform.teams.inventory.in_flight";

    // --- Repos Inventory ---
    public const string InventoryRepos = "platform.repos.inventory.count";

    // --- WorkItems Analysis ---
    public const string InventoryConsolidated = "platform.workitems.analysis.consolidated";
    public const string InventoryConsolidatedDurationMs = "platform.workitems.analysis.duration_ms";
    public const string InventoryConsolidatedErrors = "platform.workitems.analysis.errors";
    public const string DependencyLinks = "platform.workitems.analysis.links";
    public const string DependencyWorkItemsAnalysed = "platform.workitems.analysis.count";
    public const string DependenciesAnalyseDurationMs = "platform.workitems.analysis.analyse.duration_ms";
    public const string DependenciesAnalyseErrors = "platform.workitems.analysis.analyse.errors";

    // --- Operational ---
    public const string CheckpointsSaved = "platform.job.checkpoints.saved";
    public const string JobDurationMs = "platform.job.duration_ms";
    public const string JobsActive = "platform.job.active";

    // --- WorkItems Export ---
    public const string WorkItemsAttempted = "platform.workitems.export.attempted";
    public const string WorkItemsCompleted = "platform.workitems.export.completed";
    public const string WorkItemsFailed = "platform.workitems.export.failed";
    public const string WorkItemsRetried = "platform.workitems.export.retried";
    public const string WorkItemDurationMs = "platform.workitems.export.duration_ms";
    public const string FieldCount = "platform.workitems.export.fields.count";
    public const string AttachmentCount = "platform.workitems.export.attachments.count";
    public const string AttachmentDownloadDurationMs = "platform.attachments.export.duration_ms";
    public const string AttachmentDownloadBytes = "platform.attachments.export.bytes";
    public const string LinkCount = "platform.workitems.export.links.count";
    public const string RevisionCount = "platform.workitems.export.revisions.count";
    public const string PayloadBytes = "platform.workitems.export.payload.bytes";
    public const string WorkItemsInFlight = "platform.workitems.export.in_flight";
    public const string QueueDepth = "platform.workitems.export.queue_depth";
    public const string Duplicated = "platform.workitems.import.duplicated";
    public const string ChangedOnRerun = "platform.workitems.import.changed_on_rerun";
    public const string ReprocessedAfterResume = "platform.workitems.import.reprocessed_after_resume";
    public const string DuplicatedAfterResume = "platform.workitems.import.duplicated_after_resume";
    public const string MissingAfterResume = "platform.workitems.import.missing_after_resume";

    // --- WorkItems Validate ---
    public const string RevisionSourceCount = "platform.workitems.validate.revisions.source";
    public const string RevisionTargetCount = "platform.workitems.validate.revisions.target";
    public const string RevisionDelta = "platform.workitems.validate.revisions.delta";
    public const string RevisionsMissing = "platform.workitems.validate.revisions.missing";
    public const string RevisionOrderErrors = "platform.workitems.validate.revision_order_errors";
    public const string BrokenLinks = "platform.workitems.validate.broken_links";
    public const string MissingWorkItems = "platform.workitems.validate.missing";

    // --- WorkItems Transform ---
    public const string FieldTransformApplyCount = "platform.fieldtransform.apply.count";
    public const string FieldTransformApplyDurationMs = "platform.fieldtransform.apply.duration_ms";
    public const string FieldTransformApplyErrors = "platform.fieldtransform.apply.errors";
    public const string FieldTransformApplyInFlight = "platform.fieldtransform.apply.in_flight";
    public const string FieldTransformApplyFieldsModified = "platform.fieldtransform.apply.fields_modified";

    // --- Nodes Export ---
    public const string NodeExportDiscoverCount = "platform.nodes.export.discover.count";
    public const string NodeExportTreeCount = "platform.nodes.export.tree.count";
    public const string NodeExportTreeDurationMs = "platform.nodes.export.tree.duration_ms";
    public const string NodeExportTreeErrors = "platform.nodes.export.tree.errors";

    // --- Nodes Translate ---
    public const string NodeTranslateCount = "platform.nodes.import.translate.count";
    public const string NodeTranslateMapHit = "platform.nodes.import.translate.map_hit";
    public const string NodeTranslateAutoSwapHit = "platform.nodes.import.translate.autoswap_hit";
    public const string NodeTranslateExternal = "platform.nodes.import.translate.external";
    public const string NodeTranslateUnresolvable = "platform.nodes.import.translate.unresolvable";

    // --- Nodes Validate ---
    public const string NodeValidateDurationMs = "platform.nodes.validate.duration_ms";
    public const string NodeValidateUnmappedPaths = "platform.nodes.validate.unmapped_paths";
    public const string NodeValidateExternalPaths = "platform.nodes.validate.external_paths";
    public const string NodeValidateMalformedTargets = "platform.nodes.validate.malformed_targets";

    // --- Nodes Import: Replicate ---
    public const string NodeImportReplicateCount = "platform.nodes.import.replicate.count";
    public const string NodeImportReplicateAreaCount = "platform.nodes.import.replicate.area.count";
    public const string NodeImportReplicateIterationCount = "platform.nodes.import.replicate.iteration.count";
    public const string NodeImportReplicateDurationMs = "platform.nodes.import.replicate.duration_ms";
    public const string NodeImportReplicateErrors = "platform.nodes.import.replicate.errors";
    public const string NodeImportReplicateSkipped = "platform.nodes.import.replicate.skipped";
    public const string NodeImportReplicateInFlight = "platform.nodes.import.replicate.in_flight";

    // --- Nodes Import: PreCollect ---
    public const string NodeImportPreCollectCount = "platform.nodes.import.precollect.count";
    public const string NodeImportPreCollectDurationMs = "platform.nodes.import.precollect.duration_ms";
    public const string NodeImportPreCollectErrors = "platform.nodes.import.precollect.errors";
    public const string NodeImportPreCollectInFlight = "platform.nodes.import.precollect.in_flight";

    // --- Teams Export ---
    public const string TeamsExportCount = "platform.teams.export.count";
    public const string TeamsExportDurationMs = "platform.teams.export.duration_ms";
    public const string TeamsExportErrors = "platform.teams.export.errors";
    public const string TeamsExportInFlight = "platform.teams.export.in_flight";

    // --- Teams Import ---
    public const string TeamsImportCount = "platform.teams.import.count";
    public const string TeamsImportDurationMs = "platform.teams.import.duration_ms";
    public const string TeamsImportErrors = "platform.teams.import.errors";
    public const string TeamsImportInFlight = "platform.teams.import.in_flight";
    public const string TeamsImportMembersCount = "platform.teams.import.members.count";
    public const string TeamsImportMembersUnresolved = "platform.teams.import.members.unresolved";
    public const string TeamsImportIterationsCount = "platform.teams.import.iterations.count";
    public const string TeamsImportIterationsUnresolvable = "platform.teams.import.iterations.unresolvable";
    public const string TeamsImportCapacityCount = "platform.teams.import.capacity.count";
    public const string TeamsImportExtensionDurationMs = "platform.teams.import.extension.duration_ms";

    // --- Teams Validate ---
    public const string TeamsValidateCount = "platform.teams.validate.count";
    public const string TeamsValidateErrors = "platform.teams.validate.errors";

    // --- Identities Export ---
    public const string IdentitiesExportCount = "platform.identities.export.count";
    public const string IdentitiesExportDurationMs = "platform.identities.export.duration_ms";
    public const string IdentitiesExportErrors = "platform.identities.export.errors";
    public const string IdentitiesExportInFlight = "platform.identities.export.in_flight";

    // --- Identities Import ---
    public const string IdentitiesImportResolved = "platform.identities.import.resolved";
    public const string IdentitiesImportUnresolved = "platform.identities.import.unresolved";
    public const string IdentitiesImportDurationMs = "platform.identities.import.duration_ms";
    public const string IdentitiesImportErrors = "platform.identities.import.errors";

    // --- Identities Validate ---
    public const string IdentitiesValidateCount = "platform.identities.validate.count";
    public const string IdentitiesValidateErrors = "platform.identities.validate.errors";

    // --- WorkItems Prepare ---
    public const string WorkItemsPrepareResolved = "platform.workitems.prepare.resolved";
    public const string WorkItemsPrepareUnresolved = "platform.workitems.prepare.unresolved";
    public const string WorkItemsPrepareErrors = "platform.workitems.prepare.errors";
    public const string WorkItemsPrepareDurationMs = "platform.workitems.prepare.duration_ms";
    public const string WorkItemsPrepareInFlight = "platform.workitems.prepare.in_flight";

    // --- Identities Prepare ---
    public const string IdentitiesPrepareResolved = "platform.identities.prepare.resolved";
    public const string IdentitiesPrepareUnresolved = "platform.identities.prepare.unresolved";
    public const string IdentitiesPrepareErrors = "platform.identities.prepare.errors";
    public const string IdentitiesPrepareDurationMs = "platform.identities.prepare.duration_ms";
    public const string IdentitiesPrepareInFlight = "platform.identities.prepare.in_flight";

    // --- Nodes Prepare ---
    public const string NodesPrepareResolved = "platform.nodes.prepare.resolved";
    public const string NodesPrepareUnresolved = "platform.nodes.prepare.unresolved";
    public const string NodesPrepareErrors = "platform.nodes.prepare.errors";
    public const string NodesPrepareDurationMs = "platform.nodes.prepare.duration_ms";
    public const string NodesPrepareInFlight = "platform.nodes.prepare.in_flight";

    // --- Teams Prepare ---
    public const string TeamsPrepareResolved = "platform.teams.prepare.resolved";
    public const string TeamsPrepareUnresolved = "platform.teams.prepare.unresolved";
    public const string TeamsPrepareErrors = "platform.teams.prepare.errors";
    public const string TeamsPrepareDurationMs = "platform.teams.prepare.duration_ms";
    public const string TeamsPrepareInFlight = "platform.teams.prepare.in_flight";

    // --- Package Config ---
    public const string ConfigWriteCount = "platform.config.write.count";
    public const string ConfigWriteErrors = "platform.config.write.errors";
    public const string ConfigReadCount = "platform.config.read.count";
    public const string ConfigReadErrors = "platform.config.read.errors";
    public const string ConfigReadFallbacks = "platform.config.read.fallbacks";
}
