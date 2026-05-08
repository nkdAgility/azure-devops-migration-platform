# Data Model: Runtime State Categories and Resume Semantics

## 1. PackageOrchestrationState

- **Purpose**: Authoritative package-wide orchestration state shared across runs.
- **Scope**: Root `.migration/`.
- **Key fields**:
  - `phaseMarkers` (inventory/prepare/validate completion markers)
  - `planMetadata` (current execution planning metadata)
  - `packageCheckpointStores` (package-wide DB-backed state where applicable)
  - `runIndex` (reference to run folders as audit records only)
- **Validation rules**:
  - Must not contain project-scoped cursor authority.
  - Must not consume run-scoped audit files as orchestration input.

## 2. ProjectModuleResumeState

- **Purpose**: Authoritative project-scoped resume records for module execution.
- **Scope**: `/{org}/{project}/.migration/`.
- **Identity rule**: `{action}.{module}` namespace; action is mandatory in key identity.
- **Key fields**:
  - `action` (`inventory`, `export`, `import`, etc.)
  - `module` (logical module identity)
  - `lastProcessed` (relative position/key)
  - `stage` (module-specific stage marker)
  - `updatedAt` (UTC timestamp)
- **Validation rules**:
  - Action collisions are invalid.
  - Resume reads/writes must target project scope first-class, not root legacy paths.

## 3. ProcessingProgressState

- **Purpose**: Runtime progress visibility state for long-running operations.
- **Scope**: Emitted telemetry/progress channel and optionally persisted supporting state.
- **Key fields**:
  - `operation`
  - `progressUnit` (item, batch, project, etc.)
  - `completedCount`
  - `totalCount` (if known)
  - `lastUpdatedAt`
- **Validation rules**:
  - Granularity must be fine enough to demonstrate forward movement.
  - Progress cadence should not be so sparse that long-running work appears stalled.

## 4. WorkItemBatchSaveState

- **Purpose**: Durable work-item-specific checkpointing for resumable iteration.
- **Scope**: Authoritative state store within package/project checkpoint surfaces.
- **Key fields**:
  - `batchIdentity`
  - `batchStatus` (`completed`, `in-progress`, `failed`)
  - `lastWorkItemPosition`
  - `savedAt`
- **Validation rules**:
  - Persist after each completed batch.
  - Resume starts at next incomplete batch boundary.
  - Work-item-level progress must be emitted independently of batch save cadence.

## 5. RunAuditState

- **Purpose**: Immutable per-run audit evidence.
- **Scope**: `.migration/runs/<runId>/`.
- **Key fields**:
  - `jobSnapshot` (`job.json`)
  - `planSnapshot` (`plan.json`)
  - `configSnapshot` (`config.json`)
  - `logStreams`
- **Validation rules**:
  - Never authoritative for resume, phase gates, or orchestration.
  - May be used for diagnostics and audit traceability only.

## Relationships

1. `PackageOrchestrationState` governs package-level phase gates and orchestration.
2. `ProjectModuleResumeState` governs per-project resumability and depends on package-level gate outcomes.
3. `WorkItemBatchSaveState` is a specialized subtype of project-scoped resumability behavior for work-item iteration.
4. `ProcessingProgressState` reflects operational visibility across all workflows and mirrors durable state boundaries.
5. `RunAuditState` references what happened in a run but does not influence decisions.

## State Transition Notes

- Resume lifecycle:
  1. Validate package orchestration gate.
  2. Resolve project action/module resume state.
  3. For work-item operations, apply batch boundary checkpoint resume.
  4. Emit progress updates continuously at fine-grained cadence.
  5. Write run audit snapshots/logs without feeding them back into authority flow.
