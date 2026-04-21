# Feature Gap Analysis: Old Tools → New Platform

> **Old tool**: `azure-devops-migration-tools` (v16+, TFS OM & REST, direct source→target)
> **New tool**: `azure-devops-migration-platform` (Source → Package → Target, streaming)
>
> Generated: 2026-04-21

This document maps every feature in the old tool to its equivalent (or absence) in the new platform, organised by module. Status legend:

| Icon | Meaning |
|------|---------|
| ✅ | Implemented in new platform |
| 🔶 | Partially implemented or architecturally different |
| ❌ | **Missing — not yet implemented** |
| ➖ | Intentionally excluded / not applicable |

---

## 1. Work Items Module

The old tool's `TfsWorkItemMigrationProcessor` and `WorkItemTrackingProcessor` map to the new platform's **WorkItems module** (`WorkItemsModule` → `WorkItemExportOrchestrator` / `WorkItemImportOrchestrator`).

### 1.1 Core Work Item Migration

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| WIQL-scoped work item selection | `WIQLQuery` option | `wiql` scope in module config | ✅ |
| Explicit work item ID list | `WorkItemIDs` option | — | ❌ |
| Field-value regex filtering | — | `filter` scope (include/exclude with regex) | ✅ |
| Replay all revisions | `ReplayRevisions` option | `Revisions` extension (default: enabled) | ✅ |
| Collapse revisions to single item | `CollapseRevisions` option | — | ❌ |
| Maximum revision limit | `MaxRevisions` option | — | ❌ |
| Skip duplicate work items | `FilterWorkItemsThatAlreadyExistInTarget` | ID map deduplication (`idmap.db`/`idmap.json`) | ✅ |
| Reflected work item ID field | `ReflectedWorkItemIdField` | Not needed (package-based, ID map tracks source→target) | ➖ |
| Retry on work item creation | `WorkItemCreateRetryLimit` | `policies.retries.max` + exponential backoff | ✅ |
| Graceful failure tolerance | `MaxGracefulFailures` | — | ❌ |
| Pause after each item (debug) | `PauseAfterEachItem` enricher | — | ❌ |
| Attach revision history as JSON | `AttachRevisionHistory` option | — (revisions are the package; no attachment needed) | ➖ |
| Generate migration comment | `GenerateMigrationComment` option | — | ❌ |
| Skip invalid iteration paths | `SkipRevisionWithInvalidIterationPath` | — | ❌ |
| Skip invalid area paths | `SkipRevisionWithInvalidAreaPath` | — | ❌ |
| Fix HTML attachment links | `FixHtmlAttachmentLinks` | Embedded image rewriting (`EmbeddedImages` extension) | 🔶 |
| Delta detection (skip identical revisions) | — | Delta detection in `WorkItemExportOrchestrator` | ✅ |
| Streaming / memory-safe processing | Not supported (loads batches) | `IAsyncEnumerable<T>` throughout | ✅ |
| Deterministic ordering | — | Lexicographic folder names | ✅ |
| Cursor-based resume | Reflected ID dedup only | `ICheckpointingService` with cursor files | ✅ |

### 1.2 Field Mapping & Transformation

The old tool provides **14 field map types** via `FieldMappingTool`. The new platform currently treats fields as opaque values (copy-through), with identity fields handled by `IIdentityMappingService`.

| Old Field Map | Purpose | New Equivalent | Status |
|---|---|---|---|
| `FieldToFieldMap` | Copy field A → field B with optional default | — | ❌ |
| `FieldToFieldMultiMap` | Multiple source→target field mappings | — | ❌ |
| `FieldLiteralMap` | Set field to literal value | — | ❌ |
| `FieldValueMap` | Value-based transformation (e.g. State mapping) | — | ❌ |
| `FieldMergeMap` | Merge multiple fields into one | — | ❌ |
| `FieldCalculationMap` | Compute field from expression | — | ❌ |
| `FieldClearMap` | Null-out a field | — | ❌ |
| `FieldSkipMap` | Exclude field from migration | — | ❌ |
| `FieldValueToTagMap` | Convert field value to tag if matches pattern | — | ❌ |
| `FieldToTagMap` | Convert field value to tag | — | ❌ |
| `FieldToTagFieldMap` | Merge fields and add to target field | — | ❌ |
| `MultiValueConditionalMap` | Conditional multi-field mapping | — | ❌ |
| `RegexFieldMap` | Regex-based field transformation | — | ❌ |
| `TreeToTagFieldMap` | Convert area/iteration tree to tags | — | ❌ |
| Per-type `ApplyTo` filtering | Apply map only to certain work item types | — | ❌ |

### 1.3 Work Item Type Mapping

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Work item type remapping | `WorkItemTypeMappingTool` (e.g. User Story → PBI) | — | ❌ |
| Export type mapping to file | `ExportWorkItemMappingTool` | — | ❌ |
| Work item type validation | `TfsWorkItemTypeValidatorTool` | — | ❌ |

### 1.4 Links

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Related links | `WorkItemLinkEnricher` | `Links` extension (export) + link import | ✅ |
| Parent-child (hierarchical) | `WorkItemLinkEnricher` | `Links` extension | ✅ |
| External/hyperlinks | `WorkItemLinkEnricher` | `Links` extension | ✅ |
| Cross-project links | `AllowCrossProjectLinking` | Dependency discovery + link analysis | ✅ |
| Link count validation | `FilterIfLinkCountMatches` | — | ❌ |
| Save after each link | `SaveAfterEachLinkIsAdded` | — (import is atomic per revision) | ➖ |
| Outbound link checking | `OutboundLinkCheckingProcessor` | — | ❌ |
| Outbound link preservation | `KeepOutboundLinkTargetProcessor` | — | ❌ |
| Embedded links in HTML | `TfsWorkItemEmbededLinkTool` | `EmbeddedImages` extension (images only, not all links) | 🔶 |

### 1.5 Attachments

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Attachment download/upload | `WorkItemAttachmentEnricher` / `TfsAttachmentTool` | `Attachments` extension + `IAttachmentBinarySource` | ✅ |
| Streaming binary download | Not supported (temp local file) | `WriteBinaryAsync` streaming via `CryptoStream` | ✅ |
| Max attachment size limit | `MaxAttachmentSize` option | — | ❌ |
| SHA-256 integrity hash | — | Computed in-flight during download | ✅ |
| Delta detection (skip unchanged) | — | Adjacent revision URL comparison | ✅ |
| Retry with backoff | — | 8 retries with exponential backoff | ✅ |

### 1.6 Area & Iteration Paths

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Area/iteration captured as fields | Standard field capture | `System.AreaPath` / `System.IterationPath` in `revision.json` | ✅ |
| Path mapping (regex-based) | `TfsNodeStructureTool` | — | ❌ |
| Auto-create missing paths | `ShouldCreateMissingRevisionPaths` | — | ❌ |
| Replicate all nodes from source | `ReplicateAllExistingNodes` | — | ❌ |
| Language mapping (Area/Iteration) | `LanguageMaps.AreaPath` / `LanguageMaps.IterationPath` | — | ❌ |
| Convert area paths to tags | `TfsWorkItemOverwriteAreasAsTagsProcessor` | — | ❌ |

### 1.7 Identity / User Mapping

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| User identity export | `TfsExportUsersForMappingProcessor` | `IdentitiesModule` exports `descriptors.jsonl` | ✅ |
| JSON file-based user mapping | `TfsUserMappingTool` with `UserMappingFile` | `Identities/mapping.json` (operator-provided) | ✅ |
| Auto-resolve by UPN/display name | Manual JSON only | Automatic UPN/display name matching | ✅ |
| Unresolved identity tracking | — | `Identities/unresolved.json` (non-fatal) | ✅ |
| Configurable identity fields | `IdentityFieldsToCheck` (6 default fields) | All identity-typed fields auto-detected | ✅ |
| Filter to users in work items only | `OnlyListUsersInWorkItems` | — | ❌ |

### 1.8 Comments & Embedded Content

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Discussion comment history | Not available (only revision-level) | `Comments` extension (full comment edit history) | ✅ |
| Embedded image extraction | `TfsEmbededImagesTool` | `EmbeddedImages` extension | ✅ |
| Embedded image rewriting | URL replacement in HTML | Image download + URL rewrite in `revision.json` | ✅ |

### 1.9 String Manipulation

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Regex string replacement | `StringManipulatorTool` | — | ❌ |
| Invalid character cleanup | `RegexStringManipulator` | — | ❌ |

### 1.10 Changeset & Git Repo References

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Changeset-to-commit mapping | `TfsChangeSetMappingTool` | — | ❌ |
| Git repository name mapping | `TfsGitRepositoryTool` | — | ❌ |

---

## 2. Test Management Module

The old tool has three dedicated processors. The new platform has **no test management module**.

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Test plan migration | `TfsTestPlansAndSuitesMigrationProcessor` | — | ❌ |
| Test suite migration (static & dynamic) | `TfsTestPlansAndSuitesMigrationProcessor` | — | ❌ |
| Test case migration | `TfsTestPlansAndSuitesMigrationProcessor` | — | ❌ |
| Shared steps migration | `TfsTestPlansAndSuitesMigrationProcessor` | — | ❌ |
| Shared parameters migration | `TfsTestPlansAndSuitesMigrationProcessor` | — | ❌ |
| Test configuration migration | `TfsTestConfigurationsMigrationProcessor` | — | ❌ |
| Test variable migration | `TfsTestVariablesMigrationProcessor` | — | ❌ |

---

## 3. Team Settings Module

The old tool has `TfsTeamSettingsProcessor`. The new platform has a **placeholder** only (`features/export/teams/`, `features/import/teams/`).

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Team iteration paths | `TfsTeamSettingsProcessor` | — (placeholder) | ❌ |
| Team member assignments | `TfsTeamSettingsProcessor` | — (placeholder) | ❌ |
| Team capacity planning | `MigrateTeamCapacities` option | — (placeholder) | ❌ |
| Team configuration settings | `MigrateTeamSettings` option | — (placeholder) | ❌ |
| Selective team migration | `Teams` list option | — (placeholder) | ❌ |
| Export team list to file | `TfsExportTeamListProcessor` | — | ❌ |
| Create team query folders | `TfsCreateTeamFoldersProcessor` | — | ❌ |

---

## 4. Shared Queries Module

The old tool has `TfsSharedQueryProcessor`. The new platform has **no shared queries module**.

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Shared query migration | `TfsSharedQueryProcessor` | — | ❌ |
| Query folder hierarchy | Recursive folder migration | — | ❌ |
| Query project-name remapping | Query statement rewrite | — | ❌ |

---

## 5. Pipelines Module

The old tool has `AzureDevOpsPipelineProcessor`. The new platform has a **placeholder** only (`features/export/pipelines/`, `features/import/pipelines/`).

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Build pipeline migration (YAML) | `MigrateBuildPipelines` option | — (placeholder) | ❌ |
| Release pipeline migration | `MigrateReleasePipelines` option | — (placeholder) | ❌ |
| Task group migration | `MigrateTaskGroups` option | — (placeholder) | ❌ |
| Variable group migration | `MigrateVariableGroups` option | — (placeholder) | ❌ |
| Service connection migration | `MigrateServiceConnections` option | — (placeholder) | ❌ |
| Selective pipeline selection | `BuildPipelines` / `ReleasePipelines` lists | — (placeholder) | ❌ |

---

## 6. Process Definition Module

The old tool has `ProcessDefinitionProcessor`. The new platform has **no process definition module**.

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Process template migration | `ProcessDefinitionProcessor` | — | ❌ |
| Work item type definitions | `ProcessDefinitionProcessor` | — | ❌ |
| Field definitions & layouts | `ProcessDefinitionProcessor` | — | ❌ |
| States, behaviors, rules | `ProcessDefinitionProcessor` | — | ❌ |
| Page layouts & controls | `ProcessDefinitionProcessor` | — | ❌ |

---

## 7. Git Repositories Module

The new platform has a **placeholder** (`features/export/git-repos/`, `features/import/git-repos/`). The old tool has `TfsGitRepositoryTool` for name mapping but no dedicated repository migration processor.

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Git repository name mapping | `TfsGitRepositoryTool` | — (placeholder) | ❌ |
| Git repository content migration | — (not in old tool either) | — (placeholder) | ❌ |

---

## 8. Permissions Module

Neither tool has a dedicated permissions migration capability. The new platform has a **placeholder** (`features/export/permissions/`, `features/import/permissions/`).

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Project/repo ACL migration | — | — (placeholder) | ❌ |

---

## 9. Profile Pictures Module

The old tool has dedicated processors. The new platform has **no profile picture support**.

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Export profile pictures from AD | `TfsExportProfilePictureFromADProcessor` | — | ❌ |
| Import profile pictures | `TfsImportProfilePictureProcessor` | — | ❌ |

---

## 10. Bulk Operations Module

The old tool has specialised processors for in-place edits. The new platform's architecture (Source → Package → Target) does not require these, but equivalent post-import operations are absent.

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| Bulk field editing | `TfsWorkItemBulkEditProcessor` | — | ❌ |
| Work item deletion | `TfsWorkItemDeleteProcessor` | — | ➖ |
| Field overwrite (post-migration) | `TfsWorkItemOverwriteProcessor` | — | ❌ |

---

## 11. Cross-Cutting Feature Comparison

### 11.1 Configuration & Validation

| Old Feature | Old Component | New Equivalent | Status |
|---|---|---|---|
| JSON configuration | `appsettings.json` / `configuration.json` | Scenario JSON files (`scenarios/*.json`) | ✅ |
| Schema version upgrading | `OptionsConfigurationUpgrader` (V15→V17) | `configVersion: "1.0"` (single version so far) | ✅ |
| Interactive config wizard | — | `config new` command | ✅ |
| Work item type validation (pre-flight) | `TfsWorkItemTypeValidatorProcessor` | Tier-2 pre-flight validation (`ValidateAsync()`) | 🔶 |
| Env variable substitution | `%token%` in PAT fields | `$ENV:VARNAME` in authentication block | ✅ |

### 11.2 Source & Target Support

| Source/Target Type | Old Tool | New Platform | Status |
|---|---|---|---|
| Azure DevOps Services (REST) | `AzureDevOpsEndpoint` | `AzureDevOpsServices` source/target | ✅ |
| TFS on-premises (OM) | `TfsTeamProjectEndpoint` | `TeamFoundationServer` source (net481 subprocess) | ✅ |
| File system endpoint | `FileSystemWorkItemEndpoint` | `IArtefactStore` (filesystem package is the core model) | ✅ |
| Simulated/test endpoint | — | `Simulated` source/target | ✅ |

### 11.3 Authentication

| Old Feature | Old Tool | New Platform | Status |
|---|---|---|---|
| Personal Access Token (PAT) | `AccessToken` auth mode | `authentication.accessToken` | ✅ |
| Network credentials (domain) | `NetworkCredentials` mode | Windows Integrated Auth | ✅ |
| Integrated (Windows) auth | `Integrated` mode | Supported (omit auth block) | ✅ |
| Service principal | — | Cloud/Hosted topologies | ✅ |
| Env var for secrets | `%token%` | `$ENV:VARNAME` | ✅ |

### 11.4 Observability

| Old Feature | Old Tool | New Platform | Status |
|---|---|---|---|
| Serilog logging | ✅ | NDJSON structured logging (`Logs/agent.jsonl`) | ✅ |
| Application Insights | ✅ | OTel → Application Insights / Datadog | ✅ |
| OpenTelemetry metrics | Partial | Full (counters, histograms, correlation IDs) | ✅ |
| Elmah.io exception tracking | ✅ | — (OTel replaces this) | ➖ |

### 11.5 Architecture Advantages (New Platform Only)

These are capabilities the new platform provides that **do not exist** in the old tool:

| New Capability | Description |
|---|---|
| Package-based architecture | Source → Files → Target (auditable, portable, resumable) |
| Streaming import/export | `IAsyncEnumerable<T>`, no in-memory buffering |
| Control plane + agent model | Job scheduling, lease management, multi-agent |
| Terminal UI (TUI) | Live progress dashboard (Terminal.Gui) |
| Aspire integration | Cloud provisioning, Aspire dashboard for local dev |
| Multi-org inventory | `organisations` roster for cross-org discovery |
| Dependency discovery | Cross-project/cross-org link analysis |
| Comment edit history | Full comment versioning (not available in old tool) |
| Four-tier validation | Structural → Connectivity → Pre-flight → Post-flight |
| Cursor-based checkpointing | Per-module cursor files, force-fresh, interval-based flush |

---

## Summary: Missing Modules

| Module | Old Tool Status | New Platform Status | Gap |
|---|---|---|---|
| **Work Items** | Full (14 processors + 14 field maps + tools) | Full core, missing field maps & type mapping | **Field mapping engine** |
| **Test Management** | Full (3 processors) | Not started | **Entire module** |
| **Team Settings** | Full (3 processors) | Placeholder only | **Entire module** |
| **Shared Queries** | Full (1 processor) | Not started | **Entire module** |
| **Pipelines** | Full (1 processor, 5 artefact types) | Placeholder only | **Entire module** |
| **Process Definitions** | Full (1 processor) | Not started | **Entire module** |
| **Git Repositories** | Name mapping only | Placeholder only | **Entire module** |
| **Permissions** | Not implemented | Placeholder only | N/A (neither has it) |
| **Profile Pictures** | Full (2 processors) | Not started | **Entire module** |
| **Bulk Operations** | Full (3 processors) | Not applicable (different architecture) | Low priority |

## Summary: Missing Features Within Work Items

| Feature Area | Count Missing | Priority |
|---|---|---|
| Field mapping engine (14 map types) | 14 | **High** — required for cross-process-template migrations |
| Work item type mapping | 1 | **High** — required for Agile↔Scrum migrations |
| Area/iteration path mapping & creation | 5 | **High** — required for project restructuring |
| String manipulation (regex cleanup) | 2 | Medium |
| Collapse revisions option | 1 | Medium |
| Max revision limit | 1 | Low |
| Graceful failure tolerance | 1 | Medium |
| Migration comment generation | 1 | Low |
| Skip invalid area/iteration revisions | 2 | Medium |
| Max attachment size limit | 1 | Low |
| Explicit work item ID list | 1 | Low |
| Outbound link checking/preservation | 2 | Low |
| Changeset mapping | 1 | Low |
| Debug pause-after-each-item | 1 | Low |
