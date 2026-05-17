# Tasks: Work Item ID Map — Integrity, Rebuild, and Sync Support

**Spec**: `specs/019-workitem-idmap-sync/spec.md`
**Plan**: `specs/019-workitem-idmap-sync/plan.md`
**Data Model**: `specs/019-workitem-idmap-sync/data-model.md`
**Contracts**: `specs/019-workitem-idmap-sync/contracts/`
**Branch**: `019-workitem-idmap-sync`

---

## Phase 1: Foundational Abstractions & Schema (Blocking)

**Purpose**: Interface additions, new exception types, schema changes, and shared utilities that ALL user stories depend on.
All Phase 1 tasks must be complete before any user story phase begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Abstraction Layer

- [ ] T001 Extend `src/DevOpsMigrationPlatform.Abstractions/Models/IdMapEntry.cs` — add `public int? LastRevisionIndex { get; init; }` property (nullable; null = not yet tracked). See `data-model.md` → "Record: IdMapEntry". Status: incomplete
  - Evidence: IdMapEntry has SourceId/TargetId only; LastRevisionIndex is absent (src/DevOpsMigrationPlatform.Abstractions.Storage/IdMapEntry.cs).

- [ ] T002 Extend `src/DevOpsMigrationPlatform.Abstractions/Services/IIdMapStore.cs` — add 4 new method signatures with XML doc-comments: `GetLastRevisionIndexAsync`, `UpdateLastRevisionIndexAsync`, `EnumerateWorkItemMappingsAsync`, `RecordSkippedRevisionAsync`. See `contracts/IIdMapStore.md` and `data-model.md` → "Interface Changes". Status: incomplete
  - Evidence: IIdMapStore lacks EnumerateWorkItemMappingsAsync and uses different RecordSkippedRevisionAsync signature (src/DevOpsMigrationPlatform.Abstractions.Storage/IIdMapStore.cs).

- [X] T003 Extend `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportTarget.cs` — add `Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)` with XML doc-comment. See `contracts/IWorkItemImportTarget-additions.md`. Status: complete

- [ ] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Services/IPackageLockService.cs` — new interface with single method `Task<IAsyncDisposable> AcquireAsync(string packagePath, string jobId, CancellationToken ct)` and XML doc-comment. See `contracts/IPackageLockService.md` and `data-model.md` → "IPackageLockService". Status: incomplete
  - Evidence: IPackageLockService.AcquireAsync returns Task<IDisposable>, not Task<IAsyncDisposable> (src/DevOpsMigrationPlatform.Abstractions.Storage/IPackageLockService.cs).

- [X] T005 Create `src/DevOpsMigrationPlatform.Abstractions/Errors/PackageLockConflictException.cs` — sealed exception with properties `PackagePath`, `OwnerJobId`, `OwnerAgentInstanceId` (string GUID), `AcquiredAt`. Must inherit from `Exception` (not `MigrationException` — this is a startup abort, not a migration error). See `data-model.md` → "PackageLockConflictException". Status: complete

### Infrastructure — SqliteIdMapStore

- [ ] T006 Fix `SetWorkItemMappingAsync` SQL in `src/DevOpsMigrationPlatform.Infrastructure/Import/SqliteIdMapStore.cs` — change `INSERT OR REPLACE INTO work_item_map` to `INSERT INTO work_item_map (source_id, target_id) VALUES (@sid, @tid) ON CONFLICT(source_id) DO UPDATE SET target_id = excluded.target_id`. This preserves `last_revision_index` on re-seed. **BREAKING** if not done: `SeedAsync` resets revision progress on every run. See `data-model.md` → "SetWorkItemMappingAsync SQL change". Status: incomplete
  - Evidence: SetWorkItemMappingAsync still uses INSERT OR REPLACE (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/SqliteIdMapStore.cs).

- [ ] T007 Update `InitializeAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Import/SqliteIdMapStore.cs`: Status: incomplete
  - Evidence: work_item_map does not include last_revision_index and no PRAGMA schema guard is enforced (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/SqliteIdMapStore.cs).
  1. Add `skipped_revisions` table DDL to the schema creation block.
  2. Add `last_revision_index INTEGER` column to the `work_item_map` DDL.
  3. Add a schema guard after table creation: query `PRAGMA table_info(work_item_map)`, check for the `last_revision_index` column — if absent throw `InvalidOperationException("idmap.db has an outdated schema. Delete Checkpoints/idmap.db and rerun.")`. See `data-model.md` → "SQLite Schema" (all three tables) and "Schema guard".

- [ ] T008 Implement the 4 new `IIdMapStore` methods in `src/DevOpsMigrationPlatform.Infrastructure/Import/SqliteIdMapStore.cs`: Status: incomplete
  - Evidence: EnumerateWorkItemMappingsAsync is not implemented and RecordSkippedRevisionAsync contract differs from task contract (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/SqliteIdMapStore.cs).
  - `GetLastRevisionIndexAsync`: `SELECT last_revision_index FROM work_item_map WHERE source_id = @sid`
  - `UpdateLastRevisionIndexAsync`: `UPDATE work_item_map SET last_revision_index = MAX(COALESCE(last_revision_index, -1), @rev) WHERE source_id = @sid` (monotonic — never decrements)
  - `EnumerateWorkItemMappingsAsync`: streaming `SELECT source_id, target_id, last_revision_index FROM work_item_map ORDER BY source_id` via `IAsyncEnumerable<IdMapEntry>` (do NOT buffer all rows)
  - `RecordSkippedRevisionAsync`: `INSERT OR IGNORE INTO skipped_revisions (folder_path, source_id, target_id, reason, skipped_at) VALUES (...)` with `skipped_at` as ISO 8601 UTC (`DateTimeOffset.UtcNow.ToString("O")`)
  See `contracts/IIdMapStore.md` for full behaviour rules.

### Infrastructure — WorkItemExistsAsync Implementations

- [X] T009 [P] Implement `WorkItemExistsAsync` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTarget.cs` — call the ADO REST API `GET _apis/wit/workitems/{id}` via the existing work item tracking client; return `false` for a 404 (work item not found), `true` for a 200. Do NOT call `ResolveSingleAsync`. See `contracts/IWorkItemImportTarget-additions.md`. Status: complete

- [X] T010 [P] Implement `WorkItemExistsAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTarget.cs` — always return `Task.FromResult(true)` (the simulated target never deletes work items). See `contracts/IWorkItemImportTarget-additions.md`. Status: complete

### Infrastructure — PackageLockFileService

- [X] T011 Create `src/DevOpsMigrationPlatform.Infrastructure/Services/PackageLockFileService.cs` — implements `IPackageLockService`. Takes `Guid agentInstanceId` (injected — generated by `JobAgentWorker` on startup, see T040) and `IControlPlaneClient` (injected). Acquire algorithm: Status: complete/superseded; completed because superseded by specs/035-workitem-import-support
  - Evidence: Lock acquisition is implemented in ActivePackageAccess via IPackageAccess; PackageLockFileService is now an adapter (src/DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem/ActivePackageAccess.cs, src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/PackageLockFileService.cs).
  1. Build lock path = `{packagePath}/Checkpoints/agent.lock`
  2. Try `FileStream(path, FileMode.CreateNew)` — if succeeds, write JSON `{ jobId, agentInstanceId, acquiredAt }` and return handle.
  3. Handle `IOException` (file exists): read existing lock JSON to get `ownerAgentInstanceId`. Call `GET /agents/{ownerAgentInstanceId}/status` via `IControlPlaneClient`.
     - If status is Active/Running → throw `PackageLockConflictException(packagePath, ownerJobId, ownerAgentInstanceId, acquiredAt)`.
     - Otherwise (stale: 404, inactive, or network error) → delete lock file and retry from step 2 once.
  4. The returned `IAsyncDisposable` handle deletes the lock file on `DisposeAsync`. If the file is missing on dispose, emit `OTel LogWarning` and continue — best-effort cleanup only.
  Direct `FileStream` usage here is a documented justified exception (lock requires atomic OS primitive not available via `IArtefactStore`). See `contracts/IPackageLockService.md` and `data-model.md` → "agent.lock".

- [ ] T012 Register `IPackageLockService` → `PackageLockFileService` as singleton in `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformServiceExtensions.cs` (or a dedicated `InfrastructureLockServiceExtensions` extension class if preferred for clarity). Add XML doc-comment on the registration method. Status: incomplete
  - Evidence: No DI registration for IPackageLockService → PackageLockFileService was found in service extension files (repo-wide search).

### Infrastructure — WorkItemRevisionFolderParser

- [ ] T013 Create `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemRevisionFolderParser.cs` — static class with `static WorkItemRevisionFolderParseResult? TryParse(string folderName)`. Returns `null` for folders whose names do not match the `{ticks}-{workItemId}-{revisionIndex}` pattern (e.g. comment folders). `WorkItemRevisionFolderParseResult` is an internal record: `record WorkItemRevisionFolderParseResult(long Ticks, int WorkItemId, int RevisionIndex)`. No `out` parameters — encode intent in the return type (coding-standards rule 2). Replace existing scattered `folderName.Split('-')` calls in `WorkItemImportOrchestrator.cs` with calls to `WorkItemRevisionFolderParser.TryParse`. See `research.md` → "Decision 6". Status: incomplete
  - Evidence: WorkItemRevisionFolderParser exists, but WorkItemImportOrchestrator still uses folderName.Split('-') in multiple paths (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs).

### Infrastructure — RevisionProcessResult

- [X] T014 Create `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionProcessResult.cs` — internal record with a boolean `IsSkipped` and optional `string? SkipReason`: Status: complete/superseded; completed because superseded by specs/035-workitem-import-support
  - Evidence: RevisionProcessResult exists as shared abstraction in src/DevOpsMigrationPlatform.Abstractions.Agent/Export/RevisionProcessResult.cs rather than infrastructure-internal record.
  ```csharp
  internal record RevisionProcessResult(bool IsSkipped, string? SkipReason)
  {
      public static RevisionProcessResult Applied() => new(false, null);
      public static RevisionProcessResult Skipped(string reason) => new(true, reason);
  }
  ```
  This replaces the previous `SkippedRevisionException` design. No exception is thrown for skip flow — return values replace exception-as-control-flow (coding-standards rule 9: "no exceptions for control flow"). `RevisionFolderProcessor.ProcessAsync` must return `Task<RevisionProcessResult>`. See `data-model.md` → "Stage A — deleted target handling".

**Checkpoint**: All Phase 1 tasks done → compile must succeed (`dotnet build`). All subsequent phases may now begin.

---

## Phase 2: FR-017 — Exclusive Package Lock (Cross-Cutting)

**Goal**: Second agent job hard-bounces when it finds a live `Checkpoints/agent.lock`. First agent is undisturbed.

**Independent Test**: Start an import, then start a second import pointing at the same package folder. The second agent exits immediately with a structured error; the first continues normally.

### Gherkin Feature File (mandatory — write first)

- [X] T015 Create `features/platform/package-lock/exclusive-package-lock.feature` — translate FR-017 and the "concurrent agent" edge case from `spec.md` into Gherkin scenarios: Status: complete
  - Scenario: Second agent hard-bounced when live lock exists (owning agent confirmed active via ControlPlane)
  - Scenario: Stale lock (owning agent no longer active per ControlPlane) is replaced and agent proceeds normally
  - Scenario: Lock is released when job completes normally
  See `.agents/20-guardrails/workflow/acceptance-test-format.md` for format rules.

### Implementation

- [ ] T016 Inject `IPackageLockService` into `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` via constructor parameter. In `ExecuteMigrationAsync`, acquire the lock using `await _packageLockService.AcquireAsync(packagePath, job.JobId.ToString(), ct)` before any module is invoked. Use `await using` to ensure the lock handle is disposed on normal exit or exception. Let `PackageLockConflictException` bubble up — the caller will log and fail the job. See `research.md` → "Decision 3" for placement rationale. Status: incomplete
  - Evidence: JobAgentWorker does not inject or use IPackageLockService/AcquireAsync before module execution (src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs).

**Checkpoint**: FR-017 complete — concurrent-agent protection is active.

---

## Phase 3: US1 — Prevent Duplicate Work Items During Import (Priority: P1) 🎯 MVP

**Goal**: Re-running an import against a partially-migrated target never creates duplicate work items. Mappings in `idmap.db` are used. If a mapped target work item has been deleted, the revision is skipped with a structured error.

**Independent Test**: Export a package, run a partial import (interrupt after ~50% of work items), run a full import — verify no duplicates and the log shows resumed work items used existing IDs.

### Gherkin Feature File (mandatory — write first)

- [X] T017 Create `features/import/work-items/revisions/prevent-duplicate-work-items.feature` — translate User Story 1 acceptance scenarios from `spec.md` into Gherkin: Status: complete
  - Scenario: Existing mapping reused — no new work item created
  - Scenario: New mapping recorded after work item creation
  - Scenario: Partially imported work item resumes from correct stage
  - Scenario: Revision skipped when mapped target work item is deleted
  See `.agents/20-guardrails/workflow/acceptance-test-format.md` for format rules.

### Implementation

- [X] T018 Update Stage A (CreatedOrUpdated) in `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionFolderProcessor.cs` — after existing idmap lookup, add deleted-target guard: Status: complete
  1. If `idmap.GetTargetWorkItemIdAsync(sourceId, ct)` returns a value → call `target.WorkItemExistsAsync(targetId, ct)`.
  2. If target does NOT exist → call `await idMap.RecordSkippedRevisionAsync(folderPath, sourceId, targetId, "TargetWorkItemDeleted", ct)` → log structured error via `ILogger` → return `RevisionProcessResult.Skipped("TargetWorkItemDeleted")`.
  3. The orchestrator (caller) checks `result.IsSkipped`, writes cursor as Completed, and continues with the next folder.
  See `data-model.md` → "Stage A — deleted target handling".

- [ ] T019 Update `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — check `RevisionProcessResult.IsSkipped` in the revision folder loop. If skipped, write cursor as Completed for the folder and continue to the next folder (no job abort). No try/catch block is needed — this is return-value-based flow. Status: incomplete
  - Evidence: Orchestrator does not process RevisionProcessResult skip return; RevisionFolderProcessor handles skip/cursor internally and returns Task (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs).

**Checkpoint**: US1 complete — no duplicates on rerun; deleted-target folders are skipped durably.

---

## Phase 4: US2 — Rebuild ID Map from Target Provenance Markers (Priority: P1)

**Goal**: Operator can delete `idmap.db` (or start a fresh job against an already-migrated target) and the import rebuilds `idmap.db` from provenance markers before processing any revision folders.

**Independent Test**: Delete `idmap.db`, run an import against a target with migrated work items bearing provenance markers — verify `idmap.db` is rebuilt and no duplicates are created.

### Gherkin Feature File (mandatory — write first)

- [X] T020 Create `features/import/work-items/revisions/rebuild-idmap-from-target.feature` — translate User Story 2 acceptance scenarios from `spec.md` into Gherkin: Status: complete
  - Scenario: ID map rebuilt from TargetField provenance markers when idmap.db absent
  - Scenario: Rebuild merges into existing idmap.db without overwriting existing entries
  - Scenario: TargetField strategy queries custom field for source IDs
  - Scenario: TargetHyperlink strategy extracts source IDs from URL pattern
  See `.agents/20-guardrails/workflow/acceptance-test-format.md` for format rules.

### Implementation

- [X] T021 Verify (or update if needed) `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — confirm `InitializeAsync` calls the `IIdMapStoreFactory` to open/create `idmap.db`, then calls `SeedAsync` on the configured `IWorkItemResolutionStrategy` before the streaming folder loop. No new logic should be needed here if `SeedAsync` already seeds with INSERT OR IGNORE semantics — verify this is the case, and add a note if any wiring is missing. The `SetWorkItemMappingAsync` fix in T006 is the prerequisite for correctness. Status: complete

- [X] T022 Verify (or update if needed) `SeedAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Import/TargetFieldResolutionStrategy.cs` and `TargetHyperlinkResolutionStrategy.cs` — confirm seeding uses `SetWorkItemMappingAsync` (now UPSERT-safe after T006) and processes results in a streaming fashion (batches of 200, no in-memory list of all work items). See `research.md` → "Decision 1" and `spec.md` FR-007, FR-013. Status: complete

**Checkpoint**: US2 complete — fresh-start rebuild works for both TargetField and TargetHyperlink.

---

## Phase 5: US3 — Rerun Export to Pick Up New Revisions (Priority: P1)

**Goal**: After a re-export that adds new revision folders to the package, a re-import processes only the delta (new folders beyond the import cursor position).

**Independent Test**: Run a full import, export new revisions, re-import — verify the import cursor resumes correctly and only new folders are processed.

### Gherkin Feature File (mandatory — write first)

- [X] T023 Create `features/import/work-items/revisions/rerun-delta-import.feature` — translate User Story 3 acceptance scenarios from `spec.md` into Gherkin: Status: complete
  - Scenario: Import cursor resumes from last position after re-export adds new revision folders
  - Scenario: Already-imported work items are not reprocessed after delta re-import
  - Scenario: Fresh-export + fresh-job uses rebuilt ID map and processes all folders
  See `.agents/20-guardrails/workflow/acceptance-test-format.md` for format rules.

### Implementation

- [X] T024 Verify (no code change expected) that `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` streaming loop already skips folders at or before the cursor position (cursor-based checkpointing). Document this verification outcome in a code comment if the logic is non-obvious. This task exists to confirm the cursor mechanism covers the import side of US3 without additional code — if gaps are found, implement them here. Status: complete

**Checkpoint**: US3 complete — delta re-import works end-to-end with cursor + ID map.

---

## Phase 6: US4 — ID Map Integrity Check (Priority: P2)

**Goal**: At import job startup, the system streams all `idmap.db` mappings and logs a warning for each mapping that points to a non-existent target work item. No file output. No job abort.

**Independent Test**: Corrupt `idmap.db` by inserting a mapping to a non-existent target work item ID (e.g., 99999999). Run an import. Verify a `LogWarning` entry appears in `Logs/agent.jsonl` for that mapping.

### Gherkin Feature File (mandatory — write first)

- [X] T025 Create `features/import/work-items/idmap-integrity-check.feature` — translate User Story 4 acceptance scenarios from `spec.md` into Gherkin: Status: complete
  - Scenario: Integrity check warns on mapping to a non-existent target work item
  - Scenario: Integrity check completes without aborting the job
  - Scenario: Integrity check reports nothing when all mappings are valid
  - Scenario: Integrity check reports that idmap.db does not exist
  See `.agents/20-guardrails/workflow/acceptance-test-format.md` for format rules.

### Implementation

- [X] T026 Implement `CheckIntegrityAsync` as a private async method in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs`: Status: complete/superseded; completed because superseded by specs/035-workitem-import-support
  - Evidence: Integrity check behavior is implemented through IIdMapStore.CheckIntegrityAsync and invoked from ImportAsync, replacing a private orchestrator loop (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs).
  ```
  await foreach (var entry in idMapStore.EnumerateWorkItemMappingsAsync(ct))
  {
      var exists = await target.WorkItemExistsAsync(entry.TargetId, ct);
      if (!exists)
          _logger.LogWarning("[WorkItems][IntegrityCheck] Mapping {SourceId}→{TargetId} points to a non-existent target work item.", entry.SourceId, entry.TargetId);
  }
  ```
  No return value, no file output, no job abort. Exceptions from `WorkItemExistsAsync` propagate normally. See `data-model.md` → "Integrity Check — behavioral contract".

- [X] T027 Call `CheckIntegrityAsync` from `WorkItemImportOrchestrator.ImportAsync` — invoke it after `InitializeAsync` and `SeedAsync` complete, but before the streaming folder loop begins. Pass the same `CancellationToken`. See `data-model.md` → "Integrity Check" and `spec.md` FR-010. Status: complete

**Checkpoint**: US4 complete — integrity check runs at import startup and surfaces stale mappings via OTel.

---

## Phase 7: US5 — Revision-Level Progress Tracking in ID Map (Priority: P2)

**Goal**: Each work item mapping in `idmap.db` tracks the highest revision index successfully imported (`last_revision_index`). On rerun, revision folders for already-applied revisions are skipped at the folder level (before any stage processing), not just at the cursor level.

**Independent Test**: Run a partial import, then add new revision folders for an existing work item and re-import. Verify only the new revision folders are processed (the already-imported ones are skipped based on `last_revision_index`, not just the cursor).

### Gherkin Feature File (mandatory — write first)

- [X] T028 Create `features/import/work-items/revisions/revision-level-progress-tracking.feature` — translate User Story 5 acceptance scenarios from `spec.md` into Gherkin: Status: complete
  - Scenario: New revision applied when revision index exceeds last tracked index
  - Scenario: Already-applied revision skipped when revision index is at or below last tracked index
  - Scenario: Falls back to cursor-based behaviour when no last_revision_index is recorded
  See `.agents/20-guardrails/workflow/acceptance-test-format.md` for format rules.

### Implementation

- [ ] T029 Update revision folder processing in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — before dispatching a folder to the processor, parse the folder name using `WorkItemRevisionFolderParser.TryParse` (T013) to extract `workItemId` and `revisionIndex`. Call `await idMapStore.GetLastRevisionIndexAsync(workItemId, ct)`. If `revisionIndex <= lastRevisionIndex` → skip the folder (do not write a cursor entry for this skip — cursor already passed it). Comment folders (`TryParse` returns `null`) are never subject to revision-index skip logic. See `research.md` → "Decision 6". Status: incomplete
  - Evidence: Revision watermark skip still relies on manual Split parsing and writes Completed cursor on watermark skip, diverging from task contract (src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs).

- [ ] T030 Call `UpdateLastRevisionIndexAsync` in the revision folder processing success path — after all stages (A, B, C, D) complete successfully for a folder, call `await idMapStore.UpdateLastRevisionIndexAsync(sourceWorkItemId, revisionIndex, ct)`. Use `MAX` semantics (implemented in SQL — T008). Call this BEFORE writing the cursor as Completed for the folder. See `data-model.md` → "UpdateLastRevisionIndexAsync". Status: incomplete
  - Evidence: UpdateLastRevisionIndexAsync is called after _processor.ProcessAsync, while cursor completion occurs inside processor; ordering differs from task contract (orchestrator vs RevisionFolderProcessor).

**Checkpoint**: US5 complete — revision-level skip is operational; reruns only apply truly new revisions.

---

## Phase N: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Align all canonical context and architecture docs with what was implemented. Resolve all 5 discrepancies in `discrepancies.md`.

- [X] T031 Update `.agents/30-context/domains/identity-and-mapping.md` — "ID Mapping (Work Item IDs)" section: change `idmap.db (PostgreSQL Portable binary ...)` to `idmap.db (SQLite — package-local indexed storage, not a control-plane database)`. Resolves **Discrepancy 1**. Status: complete

- [ ] T032 Update `.agents/30-context/domains/checkpointing-summary.md`: Status: incomplete
  - Evidence: checkpointing-summary.md still lacks last_revision_index and package-lock behavior notes requested by task.
  1. In "Per-Module Cursors" / ID map section: change PostgreSQL reference to `idmap.db (SQLite — source workItemId → target workItemId mapping; package-local indexed storage)`. Resolves **Discrepancy 2**.
  2. In the ID Map section: add a note — `The work_item_map table also tracks last_revision_index per source work item, enabling revision-level skip logic during sync/rerun imports.` Resolves **Discrepancy 4**.
  3. Add a note about the package lock: `At import job startup, an exclusive Checkpoints/agent.lock (JSON, contains jobId + agentInstanceId GUID) is acquired via IPackageLockService. A second agent targeting the same package is hard-bounced (PackageLockConflictException) if the owning agent instance is confirmed active via ControlPlane status endpoint.`

- [ ] T033 Add "Rerun / Sync Import" section to `.agents/30-context/domains/import-streaming.md` describing: Status: incomplete
  - Evidence: import-streaming.md does not yet contain the required rerun/sync import section.
  (a) How the export cursor (`Checkpoints/workitems.cursor.json`) enables delta re-export (only new revision folders written).
  (b) How the import cursor enables delta re-import (only folders beyond the cursor are processed).
  (c) How `idmap.db` `last_revision_index` enables per-work-item skip logic for already-applied revisions within a work item.
  Resolves **Discrepancy 5**.

- [ ] T034 [P] Update `docs/cli-guide.md` — add a note (or confirm existing note) that ID map rebuild and integrity check are implicit agent-side operations triggered at import job startup, NOT explicit CLI sub-commands. No `rebuild-idmap` or `check-idmap` command exists. Resolves **Discrepancy 3**. Status: incomplete
  - Evidence: docs/cli-guide.md has no idmap rebuild/integrity startup behavior note (no idmap/rebuild/integrity matches).

- [ ] T035 Mark all 5 items in `specs/019-workitem-idmap-sync/discrepancies.md` as `Resolved`, referencing which task resolved each one (T031–T034). Status: incomplete
  - Evidence: specs/019-workitem-idmap-sync/discrepancies.md is still marked Pending and discrepancy items are not marked resolved.

- [ ] T036 Review `analysis/pending-actions.md` — remove or resolve any items that this spec addressed (idmap PostgreSQL→SQLite, revision tracking, package lock). Status: incomplete
  - Evidence: analysis/pending-actions.md still includes open T036 validation item.

- [ ] T037 Run `dotnet clean && dotnet build --no-incremental` — **MUST pass with zero errors before this task is marked done**. Status: incomplete
  - Evidence: Required dotnet clean && dotnet build --no-incremental evidence is not present in this reconciliation pass.

- [ ] T038 Run `dotnet test` — **ALL tests MUST pass before this task is marked done**. Status: incomplete
  - Evidence: Full dotnet test completion evidence is not present; prior test run stalled in-session.

- [ ] T039 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output — confirm: (a) no `PackageLockConflictException` on first run, (b) lock file created and cleaned up, (c) OTel logs show integrity check results, (d) no duplicate work items on re-run. Status: incomplete
  - Evidence: No .vscode launch profile scenario verification evidence (lock lifecycle + integrity telemetry + rerun behavior) was captured.

---

## Phase 8: Agent GUID Identity (Prerequisite for Package Lock — must complete before T011)

**Purpose**: `PackageLockFileService` depends on an `agentInstanceId` GUID that identifies the running agent across the ControlPlane. These tasks establish the source of that GUID and the ControlPlane plumbing required for stale-lock detection.

- [ ] T040 In `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` — generate `Guid AgentInstanceId = Guid.NewGuid()` once on service startup (as a `readonly` field, set in the constructor). This GUID identifies this agent process instance for the duration of its lifetime. It must be registered in DI so it can be injected into `PackageLockFileService` (T011). Log it at startup via `ILogger.LogInformation` with a structured property `{AgentInstanceId}`. Status: incomplete
  - Evidence: JobAgentWorker does not generate/log a readonly AgentInstanceId GUID at startup (src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs).

- [ ] T041 In `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` — send `agentInstanceId` on every `GET /agents/lease` poll. Add it as a query-string parameter or `X-Agent-Instance-Id` header in the existing `IControlPlaneClient.PollForLeaseAsync` call. Update the `IControlPlaneClient` interface in `DevOpsMigrationPlatform.Abstractions` to accept the `agentInstanceId` parameter. The ControlPlane must record it against the active agent record so the `GET /agents/{agentInstanceId}/status` endpoint (T042) can resolve it. Status: incomplete
  - Evidence: Lease polling path has no agentInstanceId propagation contract/header evidence in JobAgentWorker/ControlPlane client code.

- [ ] T042 Add `GET /agents/{agentInstanceId}/status` endpoint to the ControlPlane — returns HTTP 200 + JSON `{ "status": "Active" }` when the agent instance is known and active, HTTP 404 when unknown or inactive. This endpoint is consumed by `PackageLockFileService` (T011) to determine whether a stale lock's owner is still live. The response need not carry any other fields. Add the endpoint to the ControlPlane API and add a `.vscode/launch.json` entry for testing it locally. Status: incomplete
  - Evidence: ControlPlane has AgentStatusResponse model but no implemented GET /agents/{agentInstanceId}/status endpoint (controllers expose lease endpoints only).

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Foundational)  ← START HERE — no dependencies
    ↓ blocks all phases
Phase 8 (Agent GUID)    ← depends on Phase 1; T040/T041/T042 must complete before T011
Phase 2 (Package Lock)  ← depends on T004, T005, T011, T012, T040, T041, T042
Phase 3 (US1)           ← depends on T002, T003, T006, T007, T008, T010, T013, T014
Phase 4 (US2)           ← depends on T006, T008
Phase 5 (US3)           ← depends on T002, T008, T013
Phase 6 (US4)           ← depends on T002, T003, T008
Phase 7 (US5)           ← depends on T002, T008, T013
Phase N (Doc Sync)      ← depends on all implementation phases
```

### Parallel Opportunities

- T009 and T010 can run in parallel (different infrastructure projects, no dependency between them)
- T034 can run in parallel with T031–T033 (different doc files)
- Phases 2–7 can start as soon as Phase 1 is complete; they are independent of each other

### Within Each Phase

- Feature files **MUST** be written and committed before any step definitions or production code for that user story
- T006 (UPSERT fix) is a correctness-critical prerequisite for T022 (SeedAsync verification) — do not skip
- T013 (`WorkItemRevisionFolderParser`) must be done before T029 and T030 (revision skip logic uses the parser)

---

## Notes

- `[P]` = can run in parallel with other `[P]`-marked tasks in the same phase
- Feature files are ATDD Phase 1 artifacts — write them before any implementation tasks in their phase
- The `WorkItemRevisionFolderParser` (T013) refactors existing scattered `Split('-')` logic — it improves the codebase regardless of US5 and should not be deferred
- `PackageLockFileService` uses `FileStream(CreateNew)` directly — this is a documented justified exception to the `IArtefactStore` rule (same as `SqliteIdMapStore`); the lock file requires an atomic OS primitive
- The `RevisionProcessResult` (T014) is `internal` to the Infrastructure assembly — it is a typed return value replacing exception-as-control-flow; must not be exposed through the Abstractions layer
- Phases 3–5 all depend on Phase 1 being complete, but US1 (Phase 3) is the highest-value story — prioritise it first after Phase 1

