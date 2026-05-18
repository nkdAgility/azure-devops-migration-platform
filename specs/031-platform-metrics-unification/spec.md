# Specification: Platform Metrics Unification

**Status**: ⚠️ Reconciled (2026-05-17): mostly complete, with remaining gaps
**Date**: 2026-05-04
**Implemented**: 2026-05-06
**Author**: MartinHinshelwoodNKD + Copilot
**Spec dir**: `specs/031-platform-metrics-unification`
**Blocking**: Does NOT block spec-030; must be sequenced before spec-030 implementation begins (see § Sequencing)

---

## Reconciliation Snapshot (2026-05-17)

### Canonical task status source

- `tasks.md` in this folder (added during reconciliation).

### Current status summary

- **Complete**: D1, D3, D4, D5, D6, D7, D8, D11, D12
- **Incomplete**: D2, D9 and deferred breaking-policy follow-ups
- **Complete/superseded**: D10 (superseded by clean-break decision)

### Remaining incomplete work

1. Unify remaining non-`platform.*` metric instrument names in TFS object model telemetry (`WorkItemExportMetrics`, `AttachmentDownloadMetrics`).
2. Complete deferred versioning/release operations from § Breaking Change Policy.
3. Fix stale `WellKnownMetricNames` reference in `MigrationDiagnostics` XML documentation.

### Superseded list

- D10 `[Obsolete]` tombstones superseded by clean-break implementation (file deletions + `git mv` renames), as already documented in § Implementation Notes.

### Contradictions captured

1. This spec says naming is unified across all components, but TFS telemetry still uses legacy literal metric names.
2. This spec says “discovery” vocabulary is removed, while newer spec-032 still uses `JobMetrics.Discovery` terminology in planning artifacts.
3. Breaking-change version bump and release-note actions remain deferred.

### Verification evidence

- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownAgentMetricNames.cs` (`platform.*` constants)
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownControlPlaneMetricNames.cs` (`platform.job.*`)
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownCliMetricNames.cs` (`platform.command.*`)
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/WorkItemExportMetrics.cs` (legacy literals still present)
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/AttachmentDownloadMetrics.cs` (legacy literals still present)
- `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/MigrationDiagnostics.cs` (stale `WellKnownMetricNames` XML reference)
- `dotnet test tests\DevOpsMigrationPlatform.Infrastructure.Tests\DevOpsMigrationPlatform.Infrastructure.Tests.csproj --nologo --filter "FullyQualifiedName~WellKnownMetricNamesTests"` (passed, 4/4)

---

## Problem Statement

The telemetry layer currently uses **four** OTel meters with **four** incompatible string naming prefixes:

| Current meter | Current string prefix | Component | Purpose |
|---|---|---|---|
| `DevOpsMigrationPlatform.Discovery` | `discovery.*` | Agent | Inventory + dependency analysis |
| `DevOpsMigrationPlatform.Migration` | `migration.*` | Agent | Export, prepare, import, validate |
| `DevOpsMigrationPlatform.ControlPlane` | `controlplane.*` | ControlPlane | Job lifecycle (queue, in-progress, duration) |
| `DevOpsMigrationPlatform.CLI` | `cli.*` | CLI | Command invocations, duration, errors |

This fragmented state means:

1. **The concept of "discovery" is being removed from the agent vocabulary.** The agent now talks about *inventory* and *analysis*. Neither is "discovery" in the sense the Discovery meter implies.

2. **`migration` is the product name, not an action.** A metric like `migration.workitems.attempted` is ambiguous — product identity or pipeline phase? The new naming encodes the *phase* explicitly.

3. **Two agent metrics interfaces increase injection complexity.** Every class that needs both inventory and migration metrics must inject `IDiscoveryMetrics` AND `IMigrationMetrics`. A single `IPlatformMetrics` simplifies constructors and DI registration.

4. **`controlplane.*` and `cli.*` are component prefixes, not action prefixes.** They are inconsistent with each other and with the proposed `platform.<domain>.<phase>.<measure>` convention. A KQL query filtering by domain (e.g. `workitems`) must today target two different string prefixes.

5. **A single metric string prefix enables cross-component dashboards.** With all metrics under `platform.*`, operators can build dashboards that span Agent, ControlPlane, and CLI without prefix fan-out.

---

## Design Decisions

| # | Decision | Rationale | Status |
|---|---|---|---|
| D1 | Rename `Discovery` + `Migration` meters → `WellKnownMeterNames.Agent` (`DevOpsMigrationPlatform.Agent`) | Component-scoped meter; consistent with `.ControlPlane` + `.Cli` | ✅ Done |
| D2 | Unified metric string convention: `platform.<domain>.<phase>.<measure>` across ALL components | Single prefix enables cross-component dashboards; domain + phase encode "what" and "when" independently | ✅ Done |
| D3 | Merge `IDiscoveryMetrics` + `IMigrationMetrics` → single `IPlatformMetrics` (Agent) | Single injection point; no class needs two metrics interfaces | ✅ Done |
| D4 | Rename `WellKnownDiscoveryMetricNames` + `WellKnownMetricNames` → `WellKnownAgentMetricNames` | Per-component constants class; alphabetically browsable by domain | ✅ Done |
| D5 | Rename `DiscoveryMetrics` + `MigrationMetrics` → `PlatformMetrics` | Single concrete implementation wired at agent startup | ✅ Done |
| D6 | Rename `WellKnownJobMetricNames` → `WellKnownControlPlaneMetricNames` (strings: `controlplane.*` → `platform.job.*`) | Aligns ControlPlane to shared `platform.*` convention | ✅ Done |
| D7 | Rename `WellKnownCliMetricNames` strings: `cli.*` → `platform.command.*` | Aligns CLI to shared `platform.*` convention | ✅ Done |
| D8 | `WellKnownMeterNames.ControlPlane` and `.Cli` meter names are NOT changed | Meter names remain component-scoped; only the metric string values change | ✅ Done |
| D9 | Version bump: `DevOpsMigrationPlatform.Abstractions` → 4.0 | Metric names are the public contract; this is a breaking change across all components | ⚠️ Deferred — no `Version` property set in csproj (version management policy TBD) |
| D10 | Old constants deprecated (not deleted) in 4.0 with `[Obsolete]` pointing to new name | Allows gradual migration; compile-time warnings surface un-migrated usages | ❌ Superseded — author decision: clean break, old files deleted, no tombstones |
| D11 | Restructure `WellKnownTagNames` into nested static classes (`Job`, `Operation`, `WorkItem`, `Transform`, `Cli`) | Provides IDE grouping and domain clarity while keeping a single file — correlation value requires the same string across all components | ✅ Done |
| D12 | Remove `WorkItemId` and `RevisionIndex` from `WellKnownTagNames` entirely | High-cardinality identifiers must never appear as metric tags; they belong in span attributes and structured log fields only | ✅ Done |

---

## Proposed Naming Convention

```
platform.<domain>.<phase>.<measure>
```

All metric strings across Agent, ControlPlane, and CLI share this prefix and pattern.

| Segment | Values |
|---|---|
| `platform` | Fixed product prefix — applies to all components |
| `<domain>` | **Agent:** `workitems`, `nodes`, `teams`, `identities`, `repos`, `organisations`, `projects`, `config`, `fieldtransform`, `attachments` · **ControlPlane:** `job` · **CLI:** `command` |
| `<phase>` | **Agent:** `inventory`, `analysis`, `export`, `prepare`, `import`, `validate` · **ControlPlane:** `queue`, `execute` · **CLI:** `execute` |
| `<measure>` | `count`, `duration_ms`, `errors`, `in_flight`, `bytes`, `queue_depth`, `map_hit`, `unresolvable`, `invocations`, etc. |

### Constants class → meter mapping

| Constants class | Meter (`WellKnownMeterNames.*`) | Metric string prefix |
|---|---|---|
| `WellKnownAgentMetricNames` | `.Agent` (`DevOpsMigrationPlatform.Agent`) | `platform.*` (agent domains) |
| `WellKnownControlPlaneMetricNames` | `.ControlPlane` (`DevOpsMigrationPlatform.ControlPlane`) | `platform.job.*` |
| `WellKnownCliMetricNames` | `.Cli` (`DevOpsMigrationPlatform.CLI`) | `platform.command.*` |

---

## Proposed `IPlatformMetrics` Interface

```csharp
/// <summary>
/// Unified recording contract for all platform OTel metric instruments.
/// Replaces <see cref="IDiscoveryMetrics"/> and <see cref="IMigrationMetrics"/>.
/// All methods accept a pre-built <see cref="MetricsTagList"/> carrying the mandatory
/// <c>job.id</c>, <c>operation</c>, and <c>module</c> dimension tags.
/// </summary>
public interface IPlatformMetrics
{
    // --- Organisations (inventory) ---
    void OrganisationStarted(MetricsTagList tags);
    void OrganisationCompleted(MetricsTagList tags);
    void OrganisationFailed(MetricsTagList tags);
    void RecordOrganisationDuration(double milliseconds, MetricsTagList tags);
    void SetProjectCount(int count, MetricsTagList tags);

    // --- Projects (inventory) ---
    void ProjectStarted(MetricsTagList tags);
    void ProjectCompleted(MetricsTagList tags);
    void ProjectFailed(MetricsTagList tags);
    void RecordProjectDuration(double milliseconds, MetricsTagList tags);

    // --- WorkItems (inventory) ---
    void RecordWorkItemsCounted(int count, MetricsTagList tags);
    void RecordRevisionsCounted(int count, MetricsTagList tags);
    void RecordReposCounted(int count, MetricsTagList tags);

    // --- WorkItems (analysis) ---
    void RecordLinksFound(int count, MetricsTagList tags);
    void RecordWorkItemsAnalysed(int count, MetricsTagList tags);

    // --- WorkItems (export) ---
    void RecordWorkItemAttempted(MetricsTagList tags);
    void RecordWorkItemCompleted(MetricsTagList tags);
    void RecordWorkItemFailed(MetricsTagList tags);
    void RecordWorkItemRetried(MetricsTagList tags);
    void RecordWorkItemDuration(double milliseconds, MetricsTagList tags);
    void RecordFieldCount(int count, MetricsTagList tags);
    void RecordAttachmentCount(int count, MetricsTagList tags);
    void RecordLinkCount(int count, MetricsTagList tags);
    void RecordRevisionCount(int count, MetricsTagList tags);
    void RecordPayloadBytes(long bytes, MetricsTagList tags);
    void IncrementInFlight(MetricsTagList tags);
    void DecrementInFlight(MetricsTagList tags);

    // --- Attachments (export) ---
    void RecordAttachmentDownloadDuration(double milliseconds, MetricsTagList tags);
    void RecordAttachmentDownloadBytes(long bytes, MetricsTagList tags);

    // --- WorkItems (validate) ---
    void RecordRevisionSourceCount(int count, MetricsTagList tags);
    void RecordRevisionTargetCount(int count, MetricsTagList tags);
    void RecordRevisionDelta(int delta, MetricsTagList tags);
    void RecordRevisionsMissing(MetricsTagList tags);
    void RecordRevisionOrderError(MetricsTagList tags);
    void RecordBrokenLink(MetricsTagList tags);
    void RecordMissingWorkItem(MetricsTagList tags);

    // --- WorkItems (import idempotency) ---
    void RecordDuplicated(MetricsTagList tags);
    void RecordChangedOnRerun(MetricsTagList tags);
    void RecordReprocessedAfterResume(MetricsTagList tags);
    void RecordDuplicatedAfterResume(MetricsTagList tags);
    void RecordMissingAfterResume(MetricsTagList tags);

    // --- FieldTransform ---
    void RecordFieldTransformApplied(MetricsTagList tags);
    void RecordFieldTransformDuration(double milliseconds, MetricsTagList tags);
    void RecordFieldTransformError(MetricsTagList tags);
    void IncrementFieldTransformInFlight(MetricsTagList tags);
    void DecrementFieldTransformInFlight(MetricsTagList tags);
    void RecordFieldTransformFieldsModified(int count, MetricsTagList tags);

    // --- Nodes (export) ---
    void RecordNodeExportTreeCount(int count, MetricsTagList tags);
    void RecordNodeExportTreeDuration(double milliseconds, MetricsTagList tags);
    void RecordNodeExportTreeError(MetricsTagList tags);

    // --- Nodes (import translate) ---
    void RecordNodeTranslateCount(MetricsTagList tags);
    void RecordNodeTranslateMapHit(MetricsTagList tags);
    void RecordNodeTranslateAutoSwapHit(MetricsTagList tags);
    void RecordNodeTranslateExternal(MetricsTagList tags);
    void RecordNodeTranslateUnresolvable(MetricsTagList tags);

    // --- Nodes (import replicate) ---
    void RecordNodeImportReplicateCount(MetricsTagList tags);
    void RecordNodeImportReplicateAreaCount(MetricsTagList tags);
    void RecordNodeImportReplicateIterationCount(MetricsTagList tags);
    void RecordNodeImportReplicateDuration(double milliseconds, MetricsTagList tags);
    void RecordNodeImportReplicateError(MetricsTagList tags);
    void RecordNodeImportReplicateSkipped(MetricsTagList tags);
    void IncrementNodeImportReplicateInFlight(MetricsTagList tags);
    void DecrementNodeImportReplicateInFlight(MetricsTagList tags);

    // --- Nodes (import pre-collect) ---
    void RecordNodeImportPreCollectCount(MetricsTagList tags);
    void RecordNodeImportPreCollectDuration(double milliseconds, MetricsTagList tags);
    void RecordNodeImportPreCollectError(MetricsTagList tags);
    void IncrementNodeImportPreCollectInFlight(MetricsTagList tags);
    void DecrementNodeImportPreCollectInFlight(MetricsTagList tags);

    // --- Teams (export) ---
    void RecordTeamExportCount(MetricsTagList tags);
    void RecordTeamExportDuration(double milliseconds, MetricsTagList tags);
    void RecordTeamExportError(MetricsTagList tags);
    void IncrementTeamExportInFlight(MetricsTagList tags);
    void DecrementTeamExportInFlight(MetricsTagList tags);

    // --- Teams (import) ---
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

    // --- Teams (validate) ---
    void RecordTeamValidateCount(MetricsTagList tags);
    void RecordTeamValidateError(MetricsTagList tags);

    // --- Identities (export) ---
    void RecordIdentityExportCount(MetricsTagList tags);
    void RecordIdentityExportDuration(double milliseconds, MetricsTagList tags);
    void RecordIdentityExportError(MetricsTagList tags);
    void IncrementIdentityExportInFlight(MetricsTagList tags);
    void DecrementIdentityExportInFlight(MetricsTagList tags);

    // --- Identities (import) ---
    void RecordIdentityImportResolved(MetricsTagList tags);
    void RecordIdentityImportUnresolved(MetricsTagList tags);
    void RecordIdentityImportDuration(double milliseconds, MetricsTagList tags);
    void RecordIdentityImportError(MetricsTagList tags);

    // --- Identities (validate) ---
    void RecordIdentityValidateCount(MetricsTagList tags);
    void RecordIdentityValidateError(MetricsTagList tags);

    // --- Config ---
    void RecordConfigWriteCompleted(MetricsTagList tags);
    void RecordConfigWriteError(MetricsTagList tags);
    void RecordConfigReadCompleted(MetricsTagList tags);
    void RecordConfigReadError(MetricsTagList tags);
    void RecordConfigReadFallback(MetricsTagList tags);

    // --- Job (operational) ---
    void RecordCheckpointSaved(MetricsTagList tags);
    void RecordJobDuration(double milliseconds, MetricsTagList tags);
}
```

---

## Full Metric Name Mapping

### From `WellKnownDiscoveryMetricNames` → `WellKnownAgentMetricNames`

| Old Name (`discovery.*`) | New Name (`platform.*`) | Constant Name (unchanged) |
|---|---|---|
| `discovery.organisations.queued` | `platform.organisations.inventory.queued` | `OrganisationsQueued` |
| `discovery.organisations.completed` | `platform.organisations.inventory.completed` | `OrganisationsCompleted` |
| `discovery.organisations.failed` | `platform.organisations.inventory.failed` | `OrganisationsFailed` |
| `discovery.organisations.duration_ms` | `platform.organisations.inventory.duration_ms` | `OrganisationDurationMs` |
| `discovery.organisations.project_count` | `platform.organisations.inventory.project_count` | `OrganisationProjectCount` |
| `discovery.projects.queued` | `platform.projects.inventory.queued` | `ProjectsQueued` |
| `discovery.projects.completed` | `platform.projects.inventory.completed` | `ProjectsCompleted` |
| `discovery.projects.failed` | `platform.projects.inventory.failed` | `ProjectsFailed` |
| `discovery.projects.duration_ms` | `platform.projects.inventory.duration_ms` | `ProjectDurationMs` |
| `discovery.inventory.workitems` | `platform.workitems.inventory.count` | `InventoryWorkItems` |
| `discovery.inventory.revisions` | `platform.workitems.inventory.revisions` | `InventoryRevisions` |
| `discovery.inventory.repos` | `platform.repos.inventory.count` | `InventoryRepos` |
| `discovery.dependencies.links` | `platform.workitems.analysis.links` | `DependencyLinks` |
| `discovery.dependencies.workitems_analysed` | `platform.workitems.analysis.count` | `DependencyWorkItemsAnalysed` |
| `discovery.checkpoints.saved` | `platform.job.checkpoints.saved` | `CheckpointsSaved` |
| `discovery.job.duration_ms` | `platform.job.duration_ms` | `JobDurationMs` |
| `discovery.jobs.active` | `platform.job.active` | `JobsActive` |

### From `WellKnownMetricNames` → `WellKnownAgentMetricNames`

| Old Name (`migration.*`) | New Name (`platform.*`) | Constant Name (unchanged) |
|---|---|---|
| `migration.workitems.attempted` | `platform.workitems.export.attempted` | `WorkItemsAttempted` |
| `migration.workitems.completed` | `platform.workitems.export.completed` | `WorkItemsCompleted` |
| `migration.workitems.failed` | `platform.workitems.export.failed` | `WorkItemsFailed` |
| `migration.workitems.retried` | `platform.workitems.export.retried` | `WorkItemsRetried` |
| `migration.workitem.duration.ms` | `platform.workitems.export.duration_ms` | `WorkItemDurationMs` |
| `migration.workitem.fields.count` | `platform.workitems.export.fields.count` | `FieldCount` |
| `migration.workitem.attachments.count` | `platform.workitems.export.attachments.count` | `AttachmentCount` |
| `migration.attachment.download.duration.ms` | `platform.attachments.export.duration_ms` | `AttachmentDownloadDurationMs` |
| `migration.attachment.download.bytes` | `platform.attachments.export.bytes` | `AttachmentDownloadBytes` |
| `migration.workitem.links.count` | `platform.workitems.export.links.count` | `LinkCount` |
| `migration.workitem.revisions.count` | `platform.workitems.export.revisions.count` | `RevisionCount` |
| `migration.workitem.payload.bytes` | `platform.workitems.export.payload.bytes` | `PayloadBytes` |
| `migration.workitem.revisions.source.count` | `platform.workitems.validate.revisions.source` | `RevisionSourceCount` |
| `migration.workitem.revisions.target.count` | `platform.workitems.validate.revisions.target` | `RevisionTargetCount` |
| `migration.workitem.revisions.delta` | `platform.workitems.validate.revisions.delta` | `RevisionDelta` |
| `migration.workitems.revisions.missing` | `platform.workitems.validate.revisions.missing` | `RevisionsMissing` |
| `migration.workitems.revision_order_errors` | `platform.workitems.validate.revision_order_errors` | `RevisionOrderErrors` |
| `migration.workitems.broken_links` | `platform.workitems.validate.broken_links` | `BrokenLinks` |
| `migration.workitems.missing` | `platform.workitems.validate.missing` | `MissingWorkItems` |
| `migration.workitems.in_flight` | `platform.workitems.export.in_flight` | `WorkItemsInFlight` |
| `migration.queue.workitems.depth` | `platform.workitems.export.queue_depth` | `QueueDepth` |
| `migration.fieldtransform.apply.count` | `platform.fieldtransform.apply.count` | `FieldTransformApplyCount` |
| `migration.fieldtransform.apply.duration_ms` | `platform.fieldtransform.apply.duration_ms` | `FieldTransformApplyDurationMs` |
| `migration.fieldtransform.apply.errors` | `platform.fieldtransform.apply.errors` | `FieldTransformApplyErrors` |
| `migration.fieldtransform.apply.in_flight` | `platform.fieldtransform.apply.in_flight` | `FieldTransformApplyInFlight` |
| `migration.fieldtransform.apply.fields_modified` | `platform.fieldtransform.apply.fields_modified` | `FieldTransformApplyFieldsModified` |
| `migration.workitems.duplicated` | `platform.workitems.import.duplicated` | `Duplicated` |
| `migration.workitems.changed_on_rerun` | `platform.workitems.import.changed_on_rerun` | `ChangedOnRerun` |
| `migration.workitems.reprocessed_after_resume` | `platform.workitems.import.reprocessed_after_resume` | `ReprocessedAfterResume` |
| `migration.workitems.duplicated_after_resume` | `platform.workitems.import.duplicated_after_resume` | `DuplicatedAfterResume` |
| `migration.workitems.missing_after_resume` | `platform.workitems.import.missing_after_resume` | `MissingAfterResume` |
| `migration.nodes.export.discover.count` | `platform.nodes.export.discover.count` | `NodeExportDiscoverCount` |
| `migration.nodes.export.tree.count` | `platform.nodes.export.tree.count` | `NodeExportTreeCount` |
| `migration.nodes.export.tree.duration_ms` | `platform.nodes.export.tree.duration_ms` | `NodeExportTreeDurationMs` |
| `migration.nodes.export.tree.errors` | `platform.nodes.export.tree.errors` | `NodeExportTreeErrors` |
| `migration.nodes.import.translate.count` | `platform.nodes.import.translate.count` | `NodeTranslateCount` |
| `migration.nodes.import.translate.map_hit` | `platform.nodes.import.translate.map_hit` | `NodeTranslateMapHit` |
| `migration.nodes.import.translate.autoswap_hit` | `platform.nodes.import.translate.autoswap_hit` | `NodeTranslateAutoSwapHit` |
| `migration.nodes.import.translate.external` | `platform.nodes.import.translate.external` | `NodeTranslateExternal` |
| `migration.nodes.import.translate.unresolvable` | `platform.nodes.import.translate.unresolvable` | `NodeTranslateUnresolvable` |
| `migration.nodes.import.precollect.count` | `platform.nodes.import.precollect.count` | `NodeImportPreCollectCount` |
| `migration.nodes.import.precollect.duration_ms` | `platform.nodes.import.precollect.duration_ms` | `NodeImportPreCollectDurationMs` |
| `migration.nodes.import.precollect.errors` | `platform.nodes.import.precollect.errors` | `NodeImportPreCollectErrors` |
| `migration.nodes.import.precollect.in_flight` | `platform.nodes.import.precollect.in_flight` | `NodeImportPreCollectInFlight` |
| `migration.nodes.import.replicate.count` | `platform.nodes.import.replicate.count` | `NodeImportReplicateCount` |
| `migration.nodes.import.replicate.area.count` | `platform.nodes.import.replicate.area.count` | `NodeImportReplicateAreaCount` |
| `migration.nodes.import.replicate.iteration.count` | `platform.nodes.import.replicate.iteration.count` | `NodeImportReplicateIterationCount` |
| `migration.nodes.import.replicate.duration_ms` | `platform.nodes.import.replicate.duration_ms` | `NodeImportReplicateDurationMs` |
| `migration.nodes.import.replicate.errors` | `platform.nodes.import.replicate.errors` | `NodeImportReplicateErrors` |
| `migration.nodes.import.replicate.skipped` | `platform.nodes.import.replicate.skipped` | `NodeImportReplicateSkipped` |
| `migration.nodes.import.replicate.in_flight` | `platform.nodes.import.replicate.in_flight` | `NodeImportReplicateInFlight` |
| `migration.nodes.validate.duration_ms` | `platform.nodes.validate.duration_ms` | `NodeValidateDurationMs` |
| `migration.nodes.validate.unmapped_paths` | `platform.nodes.validate.unmapped_paths` | `NodeValidateUnmappedPaths` |
| `migration.nodes.validate.external_paths` | `platform.nodes.validate.external_paths` | `NodeValidateExternalPaths` |
| `migration.nodes.validate.malformed_targets` | `platform.nodes.validate.malformed_targets` | `NodeValidateMalformedTargets` |
| `migration.teams.export.count` | `platform.teams.export.count` | `TeamsExportCount` |
| `migration.teams.export.duration_ms` | `platform.teams.export.duration_ms` | `TeamsExportDurationMs` |
| `migration.teams.export.errors` | `platform.teams.export.errors` | `TeamsExportErrors` |
| `migration.teams.export.in_flight` | `platform.teams.export.in_flight` | `TeamsExportInFlight` |
| `migration.teams.import.count` | `platform.teams.import.count` | `TeamsImportCount` |
| `migration.teams.import.duration_ms` | `platform.teams.import.duration_ms` | `TeamsImportDurationMs` |
| `migration.teams.import.errors` | `platform.teams.import.errors` | `TeamsImportErrors` |
| `migration.teams.import.in_flight` | `platform.teams.import.in_flight` | `TeamsImportInFlight` |
| `migration.teams.import.members.count` | `platform.teams.import.members.count` | `TeamsImportMembersCount` |
| `migration.teams.import.members.unresolved` | `platform.teams.import.members.unresolved` | `TeamsImportMembersUnresolved` |
| `migration.teams.import.iterations.count` | `platform.teams.import.iterations.count` | `TeamsImportIterationsCount` |
| `migration.teams.import.iterations.unresolvable` | `platform.teams.import.iterations.unresolvable` | `TeamsImportIterationsUnresolvable` |
| `migration.teams.import.capacity.count` | `platform.teams.import.capacity.count` | `TeamsImportCapacityCount` |
| `migration.teams.import.extension.duration_ms` | `platform.teams.import.extension.duration_ms` | `TeamsImportExtensionDurationMs` |
| `migration.teams.validate.count` | `platform.teams.validate.count` | `TeamsValidateCount` |
| `migration.teams.validate.errors` | `platform.teams.validate.errors` | `TeamsValidateErrors` |
| `migration.identities.export.count` | `platform.identities.export.count` | `IdentitiesExportCount` |
| `migration.identities.export.duration_ms` | `platform.identities.export.duration_ms` | `IdentitiesExportDurationMs` |
| `migration.identities.export.errors` | `platform.identities.export.errors` | `IdentitiesExportErrors` |
| `migration.identities.export.in_flight` | `platform.identities.export.in_flight` | `IdentitiesExportInFlight` |
| `migration.identities.import.resolved` | `platform.identities.import.resolved` | `IdentitiesImportResolved` |
| `migration.identities.import.unresolved` | `platform.identities.import.unresolved` | `IdentitiesImportUnresolved` |
| `migration.identities.import.duration_ms` | `platform.identities.import.duration_ms` | `IdentitiesImportDurationMs` |
| `migration.identities.import.errors` | `platform.identities.import.errors` | `IdentitiesImportErrors` |
| `migration.identities.validate.count` | `platform.identities.validate.count` | `IdentitiesValidateCount` |
| `migration.identities.validate.errors` | `platform.identities.validate.errors` | `IdentitiesValidateErrors` |
| `migration.config.write.count` | `platform.config.write.count` | `ConfigWriteCount` |
| `migration.config.write.errors` | `platform.config.write.errors` | `ConfigWriteErrors` |
| `migration.config.read.count` | `platform.config.read.count` | `ConfigReadCount` |
| `migration.config.read.errors` | `platform.config.read.errors` | `ConfigReadErrors` |
| `migration.config.read.fallbacks` | `platform.config.read.fallbacks` | `ConfigReadFallbacks` |

**Total renamed:** 17 `discovery.*` + 67 `migration.*` = **84 agent metric name constants** (+ 6 ControlPlane + 3 CLI = **93 total**)

> **Note:** Constant names in `WellKnownAgentMetricNames` are deliberately preserved from their source classes. The C# identifier names do not change — only the string values change. This means callers referencing `WellKnownMetricNames.WorkItemsAttempted` need only update the `using` alias, not every call site.

---

## Files Affected

### Abstractions (breaking change layer)

| File | Change | Status |
|---|---|---|
| `WellKnownMeterNames.cs` | Add `Agent = "DevOpsMigrationPlatform.Agent"`. Remove `Discovery` + `Migration`. `ControlPlane` and `Cli` unchanged. | ✅ Done (clean break — no `[Obsolete]`) |
| `WellKnownMetricNames.cs` | ~~Mark all constants `[Obsolete]`~~ → **Deleted** (clean break) | ✅ Done |
| `WellKnownDiscoveryMetricNames.cs` | ~~Mark all constants `[Obsolete]`~~ → **Deleted via `git rm`** (renamed to `WellKnownAgentMetricNames.cs`) | ✅ Done (`git mv`) |
| `WellKnownAgentMetricNames.cs` | **RENAMED** from `WellKnownDiscoveryMetricNames.cs` — all 84 renamed agent constants | ✅ Done |
| `WellKnownJobMetricNames.cs` | ~~Mark all constants `[Obsolete]`~~ → **Deleted via `git rm`** (renamed to `WellKnownControlPlaneMetricNames.cs`) | ✅ Done (`git mv`) |
| `WellKnownControlPlaneMetricNames.cs` | **RENAMED** from `WellKnownJobMetricNames.cs` — 6 renamed ControlPlane constants | ✅ Done |
| `WellKnownCliMetricNames.cs` | Update string values from `cli.*` → `platform.command.*` | ✅ Done |
| `WellKnownTagNames.cs` | Restructure into nested classes (`Job`, `Operation`, `WorkItem`, `Transform`, `Cli`). Remove `WorkItemId` and `RevisionIndex`. | ✅ Done |
| `IDiscoveryMetrics.cs` (Abstractions.Agent) | ~~Mark `[Obsolete]`~~ → **Deleted via `git rm`** (renamed to `IPlatformMetrics.cs`) | ✅ Done (`git mv`) |
| `IMigrationMetrics.cs` (Abstractions.Agent) | ~~Mark `[Obsolete]`~~ → **Deleted** (clean break) | ✅ Done |
| `IPlatformMetrics.cs` (Abstractions.Agent) | **RENAMED** from `IDiscoveryMetrics.cs` — unified agent interface | ✅ Done |

### Infrastructure (implementation layer)

| File | Change | Status |
|---|---|---|
| `DiscoveryMetrics.cs` | ~~Mark `[Obsolete]`~~ → **Deleted via `git rm`** (renamed to `PlatformMetrics.cs`) | ✅ Done (`git mv`) |
| `MigrationMetrics.cs` (Infrastructure.Agent) | ~~Mark `[Obsolete]`~~ → **Deleted** (clean break) | ✅ Done |
| `PlatformMetrics.cs` | **RENAMED** from `DiscoveryMetrics.cs` — single `Meter(WellKnownMeterNames.Agent)` implementation | ✅ Done |
| `MigrationMetrics.cs` (TfsObjectModel) | Update to `WellKnownMeterNames.Agent` | ✅ Done |
| `AttachmentDownloadMetrics.cs` (TfsObjectModel) | Update to `WellKnownMeterNames.Agent` | ✅ Done |
| `WorkItemExportMetrics.cs` (TfsObjectModel) | Update to `WellKnownMeterNames.Agent` | ✅ Done |
| `JobLifecycleMetrics.cs` (ControlPlane) | Update string values to `platform.job.*` via `WellKnownControlPlaneMetricNames` | ✅ Done |
| CLI metrics | Update string values to `platform.command.*` via `WellKnownCliMetricNames` | ✅ Done |

### OTel host registrations

| File | Change | Status |
|---|---|---|
| `AgentOtelExtensions.cs` | Replace `WellKnownMeterNames.Discovery` + `.Migration` with `.Agent` | ✅ Done |
| `MigrationAgentServiceExtensions.cs` | Same | ✅ Done |
| `ServiceDefaults/Extensions.cs` | Same | ✅ Done |
| `MigrationPlatformHost.cs` (TfsObjectModel) | Same | ✅ Done |
| `MigrationPlatformHost.cs` (CLI.Migration) | Update DI registration: remove `IDiscoveryMetrics`/`IMigrationMetrics`, add `IPlatformMetrics` → `PlatformMetrics` | ✅ Done |

### All callers (inject-site changes only)

Any class that currently injected `IDiscoveryMetrics` or `IMigrationMetrics` now injects `IPlatformMetrics`. All ~52 call sites updated. ✅ Done

### Telemetry context doc

| File | Change | Status |
|---|---|---|
| `.agents/30-context/domains/telemetry-model.md` | Replace `IDiscoveryMetrics`/`IMigrationMetrics` references with `IPlatformMetrics`; update meter name; add meter name reference table | ✅ Done |

---

## Breaking Change Policy

Per **Coding Standard §11** and the comment in `WellKnownMetricNames.cs`:

> *"These names are the public contract — renaming is a breaking change requiring a version increment."*

This spec constitutes the version bump event. Required actions:

1. `DevOpsMigrationPlatform.Abstractions` version → `4.0.0` — ⚠️ Deferred (no `<Version>` property in csproj; version management policy TBD)
2. `DevOpsMigrationPlatform.Abstractions.Agent` version → `4.0.0` — ⚠️ Deferred (same reason)
3. Release notes document all 93 renamed metric strings and their replacements (84 agent + 6 ControlPlane + 3 CLI). — ⚠️ Deferred
4. ~~Old metric names are `[Obsolete]` in 4.0, removed in 5.0~~ — **Superseded** by clean-break approach (author decision)
5. Any Azure Monitor alert rules, KQL queries, or dashboards using `discovery.*`, `migration.*`, `controlplane.*`, or `cli.*` must be updated. (Document in runbook.) — ⚠️ Ops task, out of scope for this implementation

---

## Implementation Notes

> **Author decision (2026-05-06):** The `[Obsolete]` tombstone approach specified in D10 was **replaced** with a clean break.
> - Old files (`WellKnownMetricNames.cs`, `IMigrationMetrics.cs`, `MigrationMetrics.cs`, `MigrationMetricsTests.cs`) were **deleted** — not kept with `[Obsolete]`.
> - All renames were performed with `git mv` to preserve file history (all show as `R` in git status, not `D`+`A`).
> - 978 tests pass. Build is clean (0 errors).

---

## Impact on spec-030

Spec-030 (`IModule.InventoryAsync` / `IAnalyser`) introduces new metrics under:
- ~~Current: `discovery.inventory.workitems`, `migration.*.prepare.*`~~
- **Now available:** `platform.workitems.inventory.*`, `platform.*.prepare.*`

✅ **Sequencing complete** — spec-031 implemented first; spec-030 can use `platform.*` names from day one.

---

## ControlPlane Metric Mapping — ✅ Done

**From `WellKnownJobMetricNames` → `WellKnownControlPlaneMetricNames`**

Meter: `WellKnownMeterNames.ControlPlane` (`DevOpsMigrationPlatform.ControlPlane`) — unchanged.

| Old string (`controlplane.*`) | New string (`platform.job.*`) | C# constant name | Status |
|---|---|---|---|
| `controlplane.jobs.total` | `platform.job.queue.total` | `JobQueueTotal` | ✅ |
| `controlplane.jobs.queued` | `platform.job.queue.depth` | `JobQueueDepth` | ✅ |
| `controlplane.jobs.in_progress` | `platform.job.execute.in_progress` | `JobExecuteInProgress` | ✅ |
| `controlplane.jobs.completed` | `platform.job.execute.completed` | `JobExecuteCompleted` | ✅ |
| `controlplane.jobs.failed` | `platform.job.execute.failed` | `JobExecuteFailed` | ✅ |
| `controlplane.job.duration.ms` | `platform.job.execute.duration_ms` | `JobExecuteDurationMs` | ✅ |

---

## CLI Metric Mapping — ✅ Done

**From `WellKnownCliMetricNames` (updated in-place — string values change, file persists)**

Meter: `WellKnownMeterNames.Cli` (`DevOpsMigrationPlatform.CLI`) — unchanged.

| Old string (`cli.*`) | New string (`platform.command.*`) | C# constant name | Status |
|---|---|---|---|
| `cli.command.invocations` | `platform.command.execute.invocations` | `CommandInvocations` | ✅ |
| `cli.command.duration_ms` | `platform.command.execute.duration_ms` | `CommandDurationMs` | ✅ |
| `cli.command.errors` | `platform.command.execute.errors` | `CommandErrors` | ✅ |

---

## Out of Scope

- `IWorkItemExportMetrics` / `IAttachmentDownloadMetrics` (TfsObjectModel) — ✅ Backing meter updated to `.Agent`; interfaces unchanged
- `ProgressEvent.Metrics` / `JobMetrics` / `MigrationCounters` — CLI/TUI channel; entirely separate from OTel; not renamed
- New metric names for modules added after spec-031 lands — use the new `platform.*` convention from day one

---

## Open Questions

| # | Question | Status |
|---|---|---|
| Q1 | Should `IWorkItemExportMetrics` (TfsObjectModel) be merged into `IPlatformMetrics` or left as a TFS-specific sub-interface? | Open |
| Q2 | Should spec-030 `InventoryAsync` metrics be added to this spec's mapping table, or left to spec-030? | Resolved — left to spec-030 |
| Q3 | ~~Do we need a Prometheus/OTEL collector relabelling rule to bridge old → new metric names for existing dashboards during transition?~~ **Resolved:** Yes — the runbook must include relabelling rules for all four old prefixes (`discovery.*`, `migration.*`, `controlplane.*`, `cli.*`) until dashboards are updated. | Resolved |

