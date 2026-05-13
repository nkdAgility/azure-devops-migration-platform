# Research Findings: Work Item Import Support

**Date**: 2026-05-13 | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Overview

This document captures research findings and design decisions that inform the implementation plan. The spec provided by the user is well-detailed and includes comprehensive acceptance scenarios for all five user stories. Most clarifications have been resolved in the spec itself. Research below focuses on technical integration points with existing platform infrastructure.

---

## Existing Platform Infrastructure

### 1. Package Structure & Checkpointing

**Finding**: The package layout is canonical and non-negotiable per Constitution Principle III.

- **WorkItems folder**: `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/`
  - Each folder contains `revision.json` (metadata + field values) and inline attachment files.
  - Folders are enumerated in strict lexicographic (ascending) order.
  - This is the streaming import contract — revisions processed one folder at a time.

- **Checkpointing**: Implemented via cursor files under `.mission/Checkpoints/`.
  - Current pattern: `<modulename>.cursor.json` with fields:
    - `lastProcessed`: Relative path of last completed revision folder (or module-specific key).
    - `stage`: Enum value indicating which phase of revision processing completed.
    - `lastUpdated`: ISO 8601 timestamp.
  - Valid stages for work item import: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`.
  - Resume: Start from stage after `lastProcessed`, NOT from stage A.

- **Idempotency**: Enforced via `idmap.db` (SQLite) under `Checkpoints/`.
  - Stores mappings: source ID → target ID (work items, attachments, embedded images).
  - Prevents duplicate creation on resume.

**Integration Point**: Import module will use existing `IStateStore` abstraction to read/write cursor and idmap.db.

**Citations**: 
- `.agents/context/checkpointing-summary.md`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/StateStoreBase.cs`

---

### 2. Identity Resolution & Mapping Service

**Finding**: Identity mapping is a cross-cutting shared service, not per-module.

- **Service Contract**: `IIdentityMappingService`
  - Provides method: `ResolveIdentity(sourceId, entityType) → TargetIdentity?`
  - Returns null if no mapping exists (operator choice: block or skip).
  - No module implements its own identity resolution.

- **Mapping Source**: Pre-computed mapping artefacts stored in package (output of IdentitiesModule prepare phase).
  - Format: JSON file (location TBD in package structure).
  - Operator can review and edit mapping before import.

- **Sequencing**: IdentitiesModule must complete before any module that maps identities (enforced by configuration-driven module execution order).

**Integration Point**: Import module receives `IIdentityMappingService` via constructor injection. No direct filesystem access to mapping file.

**Citations**:
- `.agents/context/identity-and-mapping.md`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/IIdentityMappingService.cs`

---

### 3. Node Translation Tool

**Finding**: Node translation (area/iteration path remapping) is a shared tool, not per-module logic.

- **Tool Contract**: `INodeTranslationTool`
  - Method: `TranslatePath(sourceType, sourcePath) → TargetPath`
  - Applies ordered mapping rules to convert source paths → target paths.
  - Stateless pure function (no side effects, no I/O).

- **Configuration**: Defined at `MigrationPlatform.Tools.NodeTranslation` in job configuration.
  - Format: YAML or JSON rule set (TBD in configuration schema).
  - Rules evaluated in order; first match wins.

- **Application**: Called during:
  1. Node readiness phase (translating paths before creating nodes).
  2. Work item revision import (translating area/iteration fields before write).
  3. Field transformation (if field transform rule references a translated path).

**Integration Point**: Import module receives `INodeTranslationTool` via constructor. Calls it before node creation and before field writes.

**Citations**:
- `docs/configuration-reference.md` (NodeTranslation section)
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeTranslationTool.cs`

---

### 4. Field Transformation Tool

**Finding**: Field transformation is a shared tool that applies declarative rules during import.

- **Tool Contract**: `IFieldTransformTool`
  - Method: `TransformFields(workItemType, fields) → TransformedFields`
  - Applies ordered field transform groups for the given work item type.
  - Stateless pure function.

- **Configuration**: Defined at `MigrationPlatform.Tools.FieldTransform` in job configuration.
  - Format: YAML/JSON rule set (TBD in configuration schema).
  - Each rule group specifies: which field, transformation type (e.g., regex replace, map value, prepend/append), target value.

- **Execution Order** (per spec US5):
  1. NodeTranslation applied to area/iteration paths.
  2. FieldTransform applied to all other field transformations.
  3. Result written to target.

- **Error Policy**:
  - If a field transform cannot be applied (e.g., field does not exist or value type mismatch), halt with error (not silent skip).

**Integration Point**: Import module receives `IFieldTransformTool` via constructor. Calls it after node translation, before writing revised work item.

**Citations**:
- `docs/configuration-reference.md` (FieldTransform section)
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IFieldTransformTool.cs`

---

### 5. Progress Reporting & Observability

**Finding**: All progress reporting goes through `IProgressSink` (not console writes). All observability via OpenTelemetry.

- **Progress Sink**: `IProgressSink`
  - Methods: `RecordProgress(phase, processed, total)`, `RecordError(error, severity)`, `RecordWarning(warning)`.
  - Used by Job Engine to emit progress updates to CLI/TUI via HTTP → Control Plane → SSE.
  - Module code MUST NOT write to `Console` or access `System.Console` directly.

- **Telemetry** (OpenTelemetry):
  - Metrics: Work items processed, revisions applied, links created, attachments uploaded, errors per type.
  - Traces: One trace per work item; spans for node creation, revision application, field transform, link replay, attachment upload.
  - Logs: Structured logs with correlation ID (from job context) for all diagnostic messages.

**Integration Point**: Import module receives `IProgressSink` and `ILogger` via constructor. All status updates and errors reported through these abstractions.

**Citations**:
- `.agents/context/telemetry-model.md`
- `.agents/guardrails/observability-requirements.md`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Sinks/IProgressSink.cs`

---

### 6. Extension Levers & Job Configuration

**Finding**: Feature enablement is controlled via extension levers in the job configuration.

- **Lever Pattern**: Each controllable behaviour is a boolean flag in the job configuration under `Extensions.WorkItemImport.*`.
  - Examples: `RevisionReplay`, `LinkReplay`, `AttachmentReplay`, `EmbeddedImageReplay`, `FieldTransform`.
  - Operator sets levers when queuing the job via CLI or Control Plane API.

- **Parsing**: Job configuration is deserialized into `MigrationJobOptions` (or similar sealed options class).
  - ConfigureOptions validator runs before job execution.
  - Invalid lever combinations trigger early failure.

**Integration Point**: Import module receives configured options via `IOptions<WorkItemImportOptions>` constructor parameter. Levers checked at module entry point.

**Citations**:
- `docs/configuration-reference.md` (Extensions section)
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Configuration/MigrationJobOptions.cs`

---

### 7. Connector & Target System Abstractions

**Finding**: All target system I/O is abstracted; import module never calls target APIs directly.

- **Simulated Connector**: In-memory implementation of `IWorkItemService`, `INodeService`, `IAttachmentService`.
  - Fast, deterministic, no external dependencies.
  - Used for feature tests and local development.

- **AzureDevOpsServices Connector**: REST API client wrapping Azure DevOps SDK.
  - Interfaces: `IWorkItemService`, `INodeService`, `IAttachmentService`.
  - Methods: create work item, update work item, create/update node paths, upload attachments, get work item (for link resolution).

- **TeamFoundationServer Connector** (.NET 4.8 agent): TFS OM client.
  - Same interface contract as AzureDevOpsServices (for consistency).
  - Runs in isolated .NET 4.8 process; .NET 10 code does not reference it directly.
  - Communication via `TfsExporterProcessAdapter` (CLI only) or `TfsMigrationAgent` polling (import).

**Integration Point**: Import module depends on `IWorkItemService`, `INodeService`, `IAttachmentService` abstractions. Concrete implementations injected at job startup based on target connector configuration.

**Citations**:
- `docs/architecture.md` (Section 2: Execution Model)
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/IWorkItemService.cs`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/INodeService.cs`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Services/IAttachmentService.cs`

---

### 8. Package Artefact Store Interface

**Finding**: All package I/O goes through `IArtefactStore` abstraction (FileSystemArtefactStore or AzureBlobArtefactStore).

- **Interface Methods**:
  - `EnumerateAsync(path)` → `IAsyncEnumerable<string>` (relative paths in ascending order).
  - `ReadFileAsync(path)` → stream (JSON, binary).
  - `WriteFileAsync(path, stream)` → saves file.
  - `ExistsAsync(path)` → bool.

- **Lazy Enumeration**: `EnumerateAsync` results must be consumed lazily; in-memory sorting or materializing all results is forbidden.

- **Ordering Guarantee**: Both filesystem and blob implementations return paths in strict ascending (lexicographic) order. This enables deterministic revision processing.

**Integration Point**: Import module receives `IArtefactStore` via constructor. Enumerates revision folders one at a time; reads `revision.json` and attachment binaries from each folder.

**Citations**:
- `.agents/context/migration-package-concept.md`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IArtefactStore.cs`

---

## Design Decisions

### D1: Prepare Phase Validation Scope

**Decision**: Prepare phase validates:
- Required node paths exist on target OR automatic node creation is enabled.
- All referenced work item types exist on target.
- Unresolved identities are surfaced for operator review (blocking by default only if operator marks them as required).
- Required package artefacts (revision folders, attachment binaries, embedded image binaries) exist in package.
- Field values referenced by FieldTransform rules are compatible with transform operations.

**Rationale**: Preparing before import prevents partial or misleading migrations. Early detection of missing types, paths, or artefacts allows operator to correct the target or mapping before any writes occur.

**Alternative Considered**: Skip prepare and let import fail mid-stream if a blocking issue is detected.
- **Rejected**: Would result in partial target state; operator cannot safely resume without manual cleanup.

---

### D2: Stage-Aware Checkpoint Design

**Decision**: Checkpoint tracks both the current revision folder (`lastProcessed` path) AND the stage of processing for that revision.

- Stages: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`.
- On resume, import continues from the next stage after `lastProcessed` without replaying completed stages.

**Rationale**: Allows resuming mid-revision. If revision 123 was interrupted after stage 2 (AppliedFields), resume begins at stage 3 (AppliedLinks) for that revision, not from the start.

**Alternative Considered**: Checkpoint only tracks `lastProcessed` revision folder (no stage).
- **Rejected**: Would require replaying the entire revision on resume (inefficient and risky if some stages are not idempotent).

---

### D3: Identity Resolution Timing

**Decision**: Identities are resolved from the mapping service ONCE per revision (before writing to target), not per field reference.

- Resolved identities are stored in `WorkItemImportContext` for the current revision.
- Field transformation and target write reference the resolved context.

**Rationale**: Efficient (one lookup per identity), deterministic (same mapping per identity across all fields), and allows field transform to reference resolved identity values.

**Alternative Considered**: Resolve identities per field during FieldTransform.
- **Rejected**: Would create tight coupling between identity resolution and field transform; harder to test and debug.

---

### D4: Node Translation Applied Before FieldTransform

**Decision**: NodeTranslation runs first (translates area/iteration paths), then FieldTransform runs (applies declarative transforms to other fields).

- Ensures FieldTransform rules can reference the translated path values (if needed).
- Prevents field transform rules from overwriting path translations.

**Rationale**: Matches operator mental model: "First remap my structure, then apply field cleanup rules."

**Alternative Considered**: FieldTransform runs first, then NodeTranslation.
- **Rejected**: Would require field transform rules to avoid touching path fields; operator confusion and higher error risk.

---

### D5: Extension Lever Failure Modes

**Decision**:
- When revision replay is disabled: Skip entire work item revision processing (do not write work item at all for that revision).
- When link replay is disabled: Skip link application; continue with revision fields.
- When attachment replay is disabled: Skip attachment upload; continue with revision.
- When embedded image replay is disabled: Skip image binary replay; continue with revision.
- When FieldTransform is disabled: Skip transform rules; write raw exported field values.
- When FieldTransform fails: Halt run with error (not silent skip).

**Rationale**: Levers are operator-controlled on/off switches for risky operations. Disabling a lever should cleanly skip only that category. Transform failure should halt to prevent silent data loss.

**Alternative Considered**: Skip stage on error vs. halt.
- **Rejected**: Field transform failure indicates a configuration error (transform rule does not match field or type mismatch). Silent skip would hide the error and result in incomplete data.

---

### D6: Attachment & Embedded Image Identification

**Finding** (clarification from spec): Attachments and embedded images are stored inline with revision folders, not in a global package-wide collection.

- Each revision folder contains its own attachment metadata + binaries.
- Embedded images are referenced in revision field values (e.g., HTML img tags).
- During prepare, import validates that binaries referenced by metadata exist in the package.

**Integration**: Import module reads attachment metadata from `revision.json`, enumerates binaries in the revision folder, uploads them to target during `UploadedAttachments` stage, and records target attachment IDs in idmap.db.

---

## Clarifications Resolved

### Q1: Minimum work item import slice scope

**Resolution** (from spec): Revision replay, links, attachments, embedded images, NodeTranslation, FieldTransform.
- Out of scope: Comment replay, audit comments, revision-history attachments.

### Q2: Export completeness validation

**Resolution** (from spec): Prepare validates:
- Revision folders for each work item.
- Attachment binaries referenced by revisions.
- Embedded image binaries referenced by revisions.
- Field values required by FieldTransform rules.

### Q3: Identity mapping blocking behaviour

**Resolution** (from spec): Unresolved identities are surfaced for review but do not block import by default (operator can mark critical identities as blocking in prepare report).

### Q4: TFS support scope

**Resolution** (from spec assumption): TFS remains a source of package content; import support for TFS target is IN SCOPE (TfsMigrationAgent to implement import via TFS OM).

---

## Remaining Open Items

| Item | Impact | Status | Next Step |
|------|--------|--------|-----------|
| Exact attachment URI rewriting strategy | Medium | Pending spec clarification | Confirm with user during Phase 1 design review |
| Embedded image URL mapping (package URL → target URL) | Medium | Pending spec clarification | Determine in Phase 1 (likely WITURI pattern) |
| FieldTransform rule schema detail (syntax, operators) | Medium | Pending existing tool inspection | Review existing `FieldTransformTool` implementation |
| TFS OM API surface (node creation, attachment upload) | High (TFS only) | Pending TFS connector analysis | TFS implementation task (Phase 2) |

---

## Dependencies & Sequencing

### Pre-Requisites for Import

1. ✅ **IdentitiesModule**: Must complete before import (identity mappings pre-computed in package).
2. ✅ **NodesModule**: May be co-executed with import (if node readiness is not pre-created).
3. ✅ **Package**: Must contain work item revision folders, attachment/embedded-image binaries, metadata.
4. ✅ **Target Project**: Must exist; project name passed in job configuration.

### Shared Dependencies

- `IArtefactStore` (existing, already in use by export/prepare modules).
- `IStateStore` (existing; cursor + idmap.db patterns established).
- `IIdentityMappingService` (existing; IdentitiesModule populates mappings).
- `INodeTranslationTool` (existing; used by NodesModule, import reuses).
- `IFieldTransformTool` (existing or new; TBD in Phase 1).
- Target system abstractions: `IWorkItemService`, `INodeService`, `IAttachmentService` (existing for AzureDevOps; TFS OM equivalent in TfsMigrationAgent).

### No New Long-Pole Risks

All core infrastructure is in place. Import module is a consumer of existing abstractions. Main implementation work is logic binding (revision processing, stage transitions, identity resolution, transform application, attachment/image replay).

---

## Next Steps

1. ✅ **Research complete**: Move to Phase 1 (Design & Contracts).
2. ⏳ **Phase 1**: Generate `data-model.md`, `contracts/`, `quickstart.md`.
3. ⏳ **Phase 2**: Generate `tasks.md` via `/speckit.tasks` with full connector coverage allocation.
4. ⏳ **ATDD inner loop**: One scenario at a time via `/speckit.specify` → Gherkin → tests → implementation.

