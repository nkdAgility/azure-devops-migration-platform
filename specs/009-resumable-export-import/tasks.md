# Tasks: Resumable Export and Import

**Feature**: `009-resumable-export-import`  
**Branch**: `009-resumable-export-import`  
**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/cli-contracts.md](contracts/cli-contracts.md)

---

## Summary

| Metric | Value |
|---|---|
| Total tasks | 34 |
| Phase 1 — Setup / Foundational | 4 |
| Phase 2 — US1: Resume interrupted export | 7 |
| Phase 3 — US2: Resume interrupted import | 14 |
| Phase 4 — US3: Both-mode phase resume | 7 |
| Phase 5 — Polish & cross-cutting | 2 |
| Parallel opportunities | T003, T004, T007, T009, T010, T014, T020, T021, T026, T027 |
| MVP scope | Phase 1 + Phase 2 (US1 export resume is already partially implemented — adds forced fresh-start) |

---

## Phase 1: Setup / Foundational

**Purpose**: Interface and model extensions that every user story depends on. Must be complete before any story phase begins.

- [ ] T001 Add `DeleteAsync(string key, CancellationToken)` to `IStateStore` in `src/DevOpsMigrationPlatform.Abstractions/Storage/IStateStore.cs`
- [ ] T002 Implement `DeleteAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Checkpointing/FileSystemStateStore.cs` and `src/DevOpsMigrationPlatform.Infrastructure/Storage/AzureBlobArtefactStore.cs` (or equivalent `IStateStore` implementations)
- [ ] T003 [P] Add `DeleteCursorAsync(string moduleName, CancellationToken)` to `ICheckpointingService` in `src/DevOpsMigrationPlatform.Abstractions/Services/ICheckpointingService.cs`
- [ ] T004 [P] Implement `DeleteCursorAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Checkpointing/CheckpointingService.cs` — deletes `Checkpoints/<moduleName-lowercase>.cursor.json` via `IStateStore.DeleteAsync`

**Checkpoint**: `IStateStore.DeleteAsync` and `ICheckpointingService.DeleteCursorAsync` exist and compile. All existing tests pass.

---

## Phase 2: User Story 1 — Resume an Interrupted Export (Priority: P1) 🎯 MVP

**Goal**: An operator who re-runs an interrupted export skips already-exported items and completes from the cursor position. A forced fresh-start deletes the cursor and re-runs all items.

**Independent Test**: Run export against 50 work items. Interrupt after ~25. Re-run with same config — verify only the remaining items are written. Then run with `--force-fresh` — verify all 50 are re-exported.

> Note: The core export resume (`WorkItemExportOrchestrator` cursor skip/write) is already implemented. This phase adds the forced fresh-start path and the `--force-fresh` CLI flag for export.

### Gherkin Feature File (US1)

- [ ] T005 [US1] Extend `features/platform/checkpointing/cursor-resume.feature` with a new scenario: "Forced fresh-start deletes the export cursor and re-processes all items from the beginning" — translating spec.md US1 Scenario 4 into conformant Gherkin

### Implementation (US1)

- [ ] T006 [US1] Add `--force-fresh` flag to `src/DevOpsMigrationPlatform.CLI.Migration/Settings/MigrationExportCommandSettings.cs`
- [ ] T007 [P] [US1] Add `MigrationJobResume` sealed record and `ResumeMode` enum to `src/DevOpsMigrationPlatform.Abstractions/Models/MigrationJobResume.cs`
- [ ] T008 [US1] Add `Resume` property (`MigrationJobResume?`) to `MigrationJob` in `src/DevOpsMigrationPlatform.Abstractions/Models/MigrationJob.cs`
- [ ] T009 [US1] Wire `--force-fresh` into `MigrationExportCommand` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs` — set `job.Resume = new MigrationJobResume { Mode = ResumeMode.ForceFresh }` when flag is present
- [ ] T010 [US1] Handle `ResumeMode.ForceFresh` in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentWorker.cs` — call `ICheckpointingService.DeleteCursorAsync` for each registered module before running; do **not** delete `Checkpoints/idmap.json` (identity map is preserved so already-created target items are not duplicated)
- [ ] T011 [US1] Add `.vscode/launch.json` entry: `"export: force-fresh (export-ado-workitems-single-project)"` — command `devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh`

**Checkpoint**: Export with `--force-fresh` deletes cursor and re-runs all items. Default (no flag) resumes from cursor. `dotnet build` passes. `dotnet test` passes.

---

## Phase 3: User Story 2 — Resume an Interrupted Import (Priority: P1)

**Goal**: An operator who re-runs an interrupted import skips fully-processed revision folders, resumes a partially-processed folder at the correct stage, and never duplicates work items or attachments in the target.

**Independent Test**: Run import from a 50-revision-folder package. Interrupt mid-way (simulate by stopping after Stage B on folder 25). Re-run — verify folders 1–24 skipped, folder 25 resumes at Stage C, folders 26–50 run normally with no duplicates.

### Gherkin Feature Files (US2)

- [ ] T012 [US2] Extend `features/import/work-items/revisions/import-work-item-revisions.feature` with resume scenarios translating spec.md US2 Scenarios 1–4 into conformant Gherkin:
  - Scenario: Import resumes from cursor — skips folders at or before cursor position
  - Scenario: Import resumes at stage level — skips completed stages within a partially processed folder
  - Scenario: Stage A idempotency via idmap — skips work item creation if target ID already recorded
  - Scenario: Stage D idempotency via idmap — skips attachment upload if already recorded

### Foundational: Target Service Interface

- [ ] T013 [US2] Create `IWorkItemTargetService` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemTargetService.cs` with four methods: `CreateOrGetWorkItemAsync`, `ApplyFieldsAsync`, `ApplyLinksAsync`, `UploadAttachmentAsync` (see data-model.md for signatures)
- [ ] T014 [P] [US2] Create `AzureDevOpsWorkItemTargetService` stub in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemTargetService.cs` — implement `IWorkItemTargetService`; all methods throw `NotImplementedException` (shape only; full ADO REST implementation is a follow-on task within this phase)

### Foundational: Import Orchestrator

- [ ] T015 [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — streaming import engine:
  - Constructor: `IArtefactStore`, `ICheckpointingService`, `IWorkItemTargetService`, `IProgressSink?`
  - `ImportAsync`: enumerate `WorkItems/` via `IArtefactStore.EnumerateAsync` lazily (`await foreach`)
  - For each revision folder: read cursor on first call; skip folders lexicographically ≤ `cursor.LastProcessed`
  - For the resume folder (path == `cursor.LastProcessed`): start from next stage after `cursor.Stage`
  - Execute stages A→B→C→D in order; write cursor after each stage completes
  - Write cursor with `stage: "Completed"` after Stage D
  - Do not buffer revision folders in memory; no in-memory sort
  - **Emit a structured log/`IProgressSink` event when a non-null cursor is detected at startup**: include module name, `cursor.LastProcessed` path, and estimated skip count (FR-011 / SC-006); this event fires on both Auto resume and ForceFresh (where cursor was just cleared)
  - **Start an OpenTelemetry `Activity` span** for the entire `ImportAsync` call (activity name: `"WorkItemImportOrchestrator.ImportAsync"`); record `exception.type` and `exception.message` as span attributes on failure; set span status to `Error` on throw (coding standards: each module execution MUST create an activity span)
  - **Error handling**: on any stage exception, let it propagate uncaught — do NOT swallow; the cursor will remain at the last successfully written stage so the run is safely resumable; the agent worker is responsible for catching module-level exceptions and recording the failure
  - **`ConfigureAwait(false)`** MUST be applied to every `await` expression inside this class (library/module code requirement per coding standards)
- [ ] T016 [US2] Unit tests for `WorkItemImportOrchestrator` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/WorkItemImportOrchestratorTests.cs`

### Stage Implementations

- [ ] T017 [US2] Implement Stage A (`CreatedOrUpdated`) in `WorkItemImportOrchestrator`:
  - Read `Checkpoints/idmap.json` via `IStateStore` at orchestrator startup (once per run)
  - If `idmap.workItems[sourceId]` exists → use existing target ID; skip creation
  - Else → call `IWorkItemTargetService.CreateOrGetWorkItemAsync`; write new mapping to idmap; persist idmap via `IStateStore`
- [ ] T018 [US2] Implement Stage B (`AppliedFields`) in `WorkItemImportOrchestrator`:
  - Deserialise `revision.json` from `IArtefactStore.ReadAsync`
  - Call `IWorkItemTargetService.ApplyFieldsAsync` with field dictionary
- [ ] T019 [US2] Implement Stage C (`AppliedLinks`) in `WorkItemImportOrchestrator`:
  - Read link list from `revision.json`
  - Call `IWorkItemTargetService.ApplyLinksAsync`; service is responsible for skipping existing links
- [ ] T020 [P] [US2] Implement Stage D (`UploadedAttachments`) in `WorkItemImportOrchestrator`:
  - For each attachment in `revision.json`: check `idmap.attachments["workItemId:revisionIndex:relativePath"]`
  - If present → skip upload; reuse recorded target attachment ID
  - Else → read binary from `IArtefactStore`; call `IWorkItemTargetService.UploadAttachmentAsync`; write entry to idmap; persist idmap
- [ ] T021 [P] [US2] Implement `AzureDevOpsWorkItemTargetService` Stage A in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemTargetService.cs` — real ADO REST `POST /_apis/wit/workitems/{type}` call
- [ ] T022 [US2] Implement `AzureDevOpsWorkItemTargetService` Stages B, C, D — real ADO REST `PATCH` (fields/links) and `POST` (attachment upload) calls

### Step Definitions (US2)

- [ ] T023 [US2] Create Reqnroll step definitions in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/ImportWorkItemRevisionsContext.cs` and `ImportWorkItemRevisionsSteps.cs` for the feature scenarios added in T012

### Wire-up

- [ ] T024 [US2] Implement `WorkItemsModule.ImportAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — construct `WorkItemImportOrchestrator` from `ImportContext.ArtefactStore`, `ImportContext.StateStore`, and injected `IWorkItemTargetService`; call `orchestrator.ImportAsync`
- [ ] T025 [US2] Register `AzureDevOpsWorkItemTargetService` as `IWorkItemTargetService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ExportServiceCollectionExtensions.cs` (or a new `ImportServiceCollectionExtensions.cs`)

**Checkpoint**: `WorkItemsModule.ImportAsync` executes without throwing. Stages A–D run in sequence. Cursor advances per stage. Resume skips already-completed folders and stages. No duplicates on re-run. `dotnet build` passes. `dotnet test` passes.

---

## Phase 4: User Story 3 — Resume a Both-Mode Job (Priority: P2)

**Goal**: When a Both-mode job is re-run after the export phase completed, the export phase is skipped and the import phase resumes from its cursor. Forced fresh-start resets both phases.

**Independent Test**: Run `migrate` on a 20-item project. Interrupt during import. Re-run — verify export phase is skipped (no new revision folders written) and import resumes. Then run with `--force-fresh` — verify both phases re-run.

### Gherkin Feature Files (US3)

- [ ] T026 [P] [US3] Create `features/cli/execute/resume-mode.feature` with scenarios translating spec.md US3 Scenarios 1–3 into conformant Gherkin:
  - Scenario: Both-mode re-run skips completed export phase
  - Scenario: Both-mode forced fresh-start re-runs both phases
  - Scenario: Both-mode re-run where export was incomplete resumes export first

### Implementation (US3)

- [ ] T027 [P] [US3] Create `JobPhaseRecord` sealed record in `src/DevOpsMigrationPlatform.Abstractions/Checkpointing/JobPhaseRecord.cs` — fields: `ExportCompleted`, `ImportCompleted`, `UpdatedAt`
- [ ] T028 [US3] Create `PhaseTrackingService` in `src/DevOpsMigrationPlatform.Infrastructure/JobEngine/PhaseTrackingService.cs`:
  - `ReadPhaseRecordAsync`: read `Checkpoints/job.phase.json` via `IStateStore`; return default `JobPhaseRecord` if absent
  - `WritePhaseRecordAsync`: serialise `JobPhaseRecord` and write via `IStateStore`
  - `DeletePhaseRecordAsync`: call `IStateStore.DeleteAsync("Checkpoints/job.phase.json")`
  - **`ConfigureAwait(false)`** MUST be applied to every `await` expression inside this class (library/module code requirement per coding standards)
- [ ] T029 [US3] Update `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentWorker.cs` to use `PhaseTrackingService`:
  - For `Mode == Both`: read phase record before running any module
  - If `ExportCompleted == true` → skip all modules tagged as Export phase
  - After export modules complete → write `ExportCompleted = true`
  - After import modules complete → write `ImportCompleted = true`
- [ ] T030 [US3] Extend `MigrationAgentWorker` `ForceFresh` path: call `PhaseTrackingService.DeletePhaseRecordAsync` before resetting module cursors; do **not** delete `Checkpoints/idmap.json` (identity map preserved so already-created target items are not duplicated)
- [ ] T031 [US3] Add `--force-fresh` to `src/DevOpsMigrationPlatform.CLI.Migration/Settings/MigrationImportCommandSettings.cs` and wire into `MigrationImportCommand` (mirrors T006/T009 for import); also add `--force-fresh` to `MigrationMigrateCommandSettings.cs` and wire into `MigrationMigrateCommand` — `migrate` is used as a sync command where re-running continues from where export and import left off, so `--force-fresh` resets both cursors
- [ ] T032 [US3] Add `.vscode/launch.json` entries for `migrate: force-fresh` and `import: force-fresh` (see contracts/cli-contracts.md for exact profile names and commands)

**Checkpoint**: Both-mode re-run correctly skips completed export phase. Forced fresh-start works for `migrate` and `import`. `dotnet build` passes. `dotnet test` passes.

---

## Phase 5: Polish & Cross-Cutting

- [ ] T033 Rectify documentation discrepancies logged in [discrepancies.md](discrepancies.md) and update canonical docs for new flags:
  - Add "Export Cursor Behaviour" subsection to `.agents/context/checkpointing-summary.md`
  - Add `resume` block to the MigrationJob schema in `.agents/context/job-lifecycle.md`
  - Add "Both-Mode Phase Tracking" section to `.agents/context/checkpointing-summary.md`
  - Add `--force-fresh` to the `export`, `import`, and `migrate` command descriptions in `.agents/context/cli-commands.md` (both the command table and a new "Resume Options" sub-section listing the flag, its default, and its semantics)
  - Add `--force-fresh` to the `export`, `import`, and `migrate` command descriptions in `docs/cli-guide.md` and add `--force-fresh` example invocations to the Examples section
- [ ] T034 Run `dotnet clean && dotnet build --no-incremental` and `dotnet test` — confirm all pass; run `scenarios/export-ado-workitems-single-project.json` via `launch.json` profile and verify observable output shows resume behaviour

---

## Dependencies

```
T001 → T002 → T004
T001 → T003 → T004

T004 → T010 (ForceFresh path needs DeleteCursorAsync)
T007 → T008 → T009 → T010
T009 → T011

T013 → T014 → T015 → T016
T015 → T017 → T018 → T019 → T020
T015 → T023
T017 → T021 → T022
T020 → T022
T022 → T024 → T025

T027 → T028 → T029 → T030
T029 → T031 → T032
T008 → T029 (MigrationJob.Resume needed by worker)

T005, T006, T007, T009, T011 → Phase 2 complete
T012, T013..T025 → Phase 3 complete
T026..T032 → Phase 4 complete
T033, T034 → Done
```

## Parallel Execution Opportunities

| Group | Tasks | Condition |
|---|---|---|
| Foundational interfaces | T003, T007 | Both modify different files; no shared dependency |
| Target service stub + model | T014, T027 | Different files; both unblock later tasks |
| Stage D + ADO Stage A | T020, T021 | Different files within same orchestrator phase |
| Feature files | T026, T005 | Different feature files; no shared dependency |

## Implementation Strategy

**MVP** = Phase 1 + Phase 2 (export forced fresh-start + `--force-fresh` CLI flag). This is immediately demonstrable: an interrupted export can be force-reset and re-run cleanly.

**Next increment** = Phase 3 (import resume). Core pain point for data integrity on large imports.

**Then** = Phase 4 (Both-mode) + Phase 5 (operator visibility). Lower risk, high operator confidence value.

**Finally** = Phase 6 (docs + final build/test gate).
