# Tasks: Azure DevOps Work Items Import

**Input**: Design documents from `/specs/013-ado-workitems-import/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/IWorkItemImportTarget.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

**Note on unit tests**: Per Constitution VIII (ATDD inner loop Phase 3), each implementation task implicitly includes writing unit tests for every method with branching logic, calculation, or state transformation. Unit test files (e.g., `RevisionFolderProcessorTests.cs`, `SqliteIdMapStoreTests.cs`) are created alongside the production code within the same task — they are not listed as separate tasks.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the `Microsoft.Data.Sqlite` dependency, create the import scenario config, and add the `.vscode/launch.json` import debug profile.

- [x] T001 Add `Microsoft.Data.Sqlite` package reference to `Directory.Packages.props` and to `src/DevOpsMigrationPlatform.Infrastructure/DevOpsMigrationPlatform.Infrastructure.csproj`
- [x] T002 [P] Create import scenario config file at `scenarios/import-ado-workitems-single-project.json` with `mode: Import`, `target.type: Simulated`, and WorkItems module with all extensions enabled
- [x] T003 [P] Add `.vscode/launch.json` debug profile for import scenario (name: `📥 Migration CLI: Queue Import (Simulated)`, args: `queue --config scenarios/import-ado-workitems-single-project.json`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create all abstraction interfaces, model records, and the core infrastructure types that ALL user stories depend on. No user story work can begin until this phase is complete.

**⚠️ CRITICAL**: No user story implementation can begin until this phase is complete.

- [x] T004 Create `IdMapEntry` record in `src/DevOpsMigrationPlatform.Abstractions/Models/IdMapEntry.cs` with `SourceId` (int) and `TargetId` (int) properties
- [x] T005 [P] Create `AttachmentMapEntry` record in `src/DevOpsMigrationPlatform.Abstractions/Models/AttachmentMapEntry.cs` with `SourceWorkItemId`, `RevisionIndex`, `RelativePath`, `TargetAttachmentId` properties
- [x] T006 [P] Create `ImportedWorkItemResult` record in `src/DevOpsMigrationPlatform.Abstractions/Models/ImportedWorkItemResult.cs` with `TargetWorkItemId` (int) and `IsNewlyCreated` (bool) properties
- [x] T007 [P] Create `EmbeddedImageMetadata` record in `src/DevOpsMigrationPlatform.Abstractions/Models/EmbeddedImageMetadata.cs` with `OriginalUrl`, `RelativePath`, `Extension`, `Sha256`, `Size` properties
- [x] T008 [P] Create `WorkItemRelations` record in `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemRelations.cs` with `RelatedLinks`, `ExternalLinks`, `Hyperlinks` read-only list properties
- [x] T009 [P] Add `EmbeddedImages` property (`IReadOnlyList<EmbeddedImageMetadata>`) to `WorkItemRevision` record in `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemRevision.cs`
- [x] T010 Create `IIdMapStore` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IIdMapStore.cs` with `InitializeAsync`, `GetTargetWorkItemIdAsync`, `SetWorkItemMappingAsync`, `GetAttachmentIdAsync`, `SetAttachmentMappingAsync`, `SeedWorkItemMappingsAsync` methods per contracts/IWorkItemImportTarget.md
- [x] T011 [P] Create `IWorkItemImportTarget` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportTarget.cs` with `CreateWorkItemAsync`, `UpdateFieldsAsync`, `AddLinksAsync`, `UploadAttachmentAsync`, `UploadEmbeddedImageAsync`, `CreateCommentAsync`, `GetExistingRelationsAsync` methods per contracts/IWorkItemImportTarget.md
- [x] T012 [P] Create `IWorkItemImportTargetFactory` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportTargetFactory.cs` with `CreateAsync(orgUrl, project, accessToken, ct)` method
- [x] T013 [P] Create `IWorkItemResolutionStrategy` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemResolutionStrategy.cs` with `SeedAsync`, `ResolveSingleAsync`, `WriteProvenanceAsync` methods per contracts/IWorkItemImportTarget.md
- [x] T014 Implement `SqliteIdMapStore` in `src/DevOpsMigrationPlatform.Infrastructure/Import/SqliteIdMapStore.cs` — creates/opens `Checkpoints/idmap.db` via `IArtefactStore`, implements `IIdMapStore` with `work_item_map` and `attachment_map` tables per data-model.md SQLite schema
- [x] T015 [P] Implement `NullResolutionStrategy` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/NullResolutionStrategy.cs` — `SeedAsync` is no-op, `ResolveSingleAsync` returns null, `WriteProvenanceAsync` is no-op (FR-023 default fallback)
- [x] T015b [P] Implement `SimulatedWorkItemImportTarget` in `src/DevOpsMigrationPlatform.Infrastructure/Import/SimulatedWorkItemImportTarget.cs` — minimal in-memory `IWorkItemImportTarget` for offline testing. `CreateWorkItemAsync` assigns auto-incrementing target IDs, validates fields are non-empty, returns `ImportedWorkItemResult`. `UpdateFieldsAsync`, `AddLinksAsync`, `CreateCommentAsync` validate inputs and log calls. `UploadAttachmentAsync` / `UploadEmbeddedImageAsync` return deterministic fake URLs. `GetExistingRelationsAsync` returns empty `WorkItemRelations`. Registers via `AddSimulatedImportServices()` when `target.type == "Simulated"`. Required by T002/T050 scenario config.

**Checkpoint**: All interfaces and foundational types exist. User story implementation can begin.

---

## Phase 3: User Story 1 — Import Work Items from Exported Package (Priority: P1) 🎯 MVP

**Goal**: Implement the core streaming import loop: enumerate revision folders, process each through 4 stages (Create → Fields → Links → Attachments), write cursor after each stage, record ID mappings. This is the complete end-to-end import path.

**Independent Test**: Import a package with revision folders into a simulated target. Verify work items are created with correct fields, links, and attachments. Verify idmap.db records source→target mappings.

### Gherkin Feature File for User Story 1 (mandatory)

- [x] T016 [US1] Create `features/import/work-items/revisions/streaming-replay.feature` — translate spec.md User Story 1 acceptance scenarios 1–6 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Import Work Item Revisions via Streaming Replay)

### Implementation for User Story 1

- [x] T017 [US1] Implement `RevisionFolderProcessor` in `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionFolderProcessor.cs` — 4-stage processing (CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments) for a single revision folder. Constructor receives `IWorkItemImportTarget`, `IIdMapStore`, `ICheckpointingService`, `IIdentityMappingService`, `IArtefactStore`. Reads `revision.json`, deserializes to `WorkItemRevision`, delegates to target abstraction. Writes cursor via `ICheckpointingService` after each stage. Checks `IIdMapStore` before Stage A creation and Stage D upload. Respects ALL extension enabled flags: skip Stage C if `Links: false`, skip Stage D if `Attachments: false`, skip inline comments if `Comments: false`, skip embedded image processing if `EmbeddedImages: false`. If `Revisions: false`, skip 4-stage processing entirely (caller handles simplified path). `IIdentityMappingService` is accepted as a dependency but pass-through (unused) until T031 extends Stage B.
- [x] T018 [US1] Implement `WorkItemImportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — streaming import loop. Constructor receives `IArtefactStore`, `ICheckpointingService`, `IProgressSink`, `IWorkItemResolutionStrategy`, `RevisionFolderProcessor`. On `ImportAsync`: reads cursor, calls `IWorkItemResolutionStrategy.SeedAsync()`, enumerates `IArtefactStore.EnumerateAsync("WorkItems/")` lazily, skips folders <= cursor, parses folder name to distinguish revision vs comment folders, delegates to `RevisionFolderProcessor` for revisions, emits `ProgressEvent` after each folder. If `Revisions` extension is disabled, skip revision folder processing entirely (import only creates work items at latest state via a simplified path). If `Comments: false`, skip all comment folder processing.
- [x] T019 [US1] Implement `AzureDevOpsWorkItemImportTarget` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTarget.cs` — wraps `WorkItemTrackingHttpClient`. `CreateWorkItemAsync` builds `JsonPatchDocument` with field `add` operations, calls SDK `CreateWorkItemAsync`. `UpdateFieldsAsync` builds patch doc with field values. `AddLinksAsync` adds relation entries, calls `GetExistingRelationsAsync` internally to skip duplicates. `UploadAttachmentAsync` calls SDK `CreateAttachmentAsync` then adds attachment relation. `CreateCommentAsync` calls SDK `AddCommentAsync`. `GetExistingRelationsAsync` calls SDK `GetWorkItemAsync(expand: Relations)`. All methods propagate `CancellationToken`. Uses Polly retry with exponential back-off.
- [x] T020 [US1] Implement `AzureDevOpsWorkItemImportTargetFactory` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTargetFactory.cs` — creates `WorkItemTrackingHttpClient` from orgUrl/pat, returns `AzureDevOpsWorkItemImportTarget` instance
- [x] T021 [US1] Create `ImportServiceCollectionExtensions` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ImportServiceCollectionExtensions.cs` — `AddAzureDevOpsImportServices(this IServiceCollection)` registers `IWorkItemImportTargetFactory`, `IIdMapStore` (SqliteIdMapStore), and default `IWorkItemResolutionStrategy` (NullResolutionStrategy). Resolution strategy selection based on module extensions config (keyed or factory pattern).
- [x] T022 [US1] Wire `WorkItemsModule.ImportAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — replace `NotSupportedException` stub. Add constructor parameters for import dependencies (`IWorkItemImportTargetFactory`, `IIdMapStore`, `IWorkItemResolutionStrategy`). Create `WorkItemImportOrchestrator` and call `ImportAsync`. Add `"Identities"` to `DependsOn` list.
- [x] T023 [US1] Enable import mode in `QueueCommand` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` — replace `ExecuteImportStub()` with the same job-submission flow used for export (mode-agnostic: job `mode` field determines export vs import on the agent side). Validate that `target` block is present in config when mode is `Import` or `Both`.
- [x] T024 [US1] Register import services in the CLI host builder — call `AddAzureDevOpsImportServices()` from the appropriate `MigrationPlatformHost` setup method so import dependencies are available when the agent processes an import job.

**Checkpoint**: Core import works end-to-end — revision folders are streamed in order, work items created/updated, fields applied, links added, attachments uploaded, cursor tracked, idmap populated. User Story 1 is independently testable.

---

## Phase 4: User Story 2 — Resumable Import with Cursor-Based Checkpointing (Priority: P1)

**Goal**: Ensure the import is fully resumable — interrupted imports restart from the last cursor position and stage without reprocessing.

**Independent Test**: Import a package, interrupt mid-way (simulate crash by stopping the process), restart, verify the cursor skips completed work and resumes from the correct stage within the interrupted folder.

### Gherkin Feature File for User Story 2 (mandatory)

- [x] T025 [US2] Create `features/platform/checkpointing/import-cursor-resume.feature` — translate spec.md User Story 2 acceptance scenarios 1–3 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Import Cursor Resume)

### Implementation for User Story 2

- [x] T026 [US2] Add mid-folder resume logic to `RevisionFolderProcessor.ProcessAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionFolderProcessor.cs` — when the cursor `lastProcessed` matches the current folder and `stage` is not `Completed`, skip stages already completed (e.g. if stage is `AppliedFields`, start from `AppliedLinks`). This extends the stage loop added in T017.
- [x] T027 [US2] Add `--force-fresh` handling for import cursor in `WorkItemImportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — when `MigrationJob.Resume.Mode` is `ForceFresh`, delete `Checkpoints/workitems.cursor.json` via `IStateStore` before starting enumeration. Preserve `idmap.db` (do not delete).

**Checkpoint**: Import resumes correctly from any interrupted stage. `--force-fresh` resets the cursor but preserves the ID map.

---

## Phase 5: User Story 3 — Streaming Memory-Safe Import (Priority: P1)

**Goal**: Verify and enforce that the import processes one revision folder at a time with constant memory usage.

**Independent Test**: Profile memory during import of a large simulated package (20k+ folders). Memory must not grow proportionally to folder count.

### Gherkin Feature File for User Story 3 (mandatory)

- [x] T028 [US3] Create `features/import/work-items/revisions/streaming-memory-safety.feature` — translate spec.md User Story 3 acceptance scenarios 1–3 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Streaming Memory-Safe Import)

### Implementation for User Story 3

- [x] T029 [US3] Verify `WorkItemImportOrchestrator` streaming compliance in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — confirm `EnumerateAsync` is consumed via `await foreach` with no `.ToListAsync()` or `.ToArrayAsync()` materialisation. Confirm `RevisionFolderProcessor` reads one `revision.json` at a time via `IArtefactStore.ReadAsync` and does not accumulate revision data across iterations. Confirm attachment uploads use `IArtefactStore.ReadBinaryAsync` → `Stream` → `IWorkItemImportTarget.UploadAttachmentAsync` with no intermediate `byte[]` buffer.

**Checkpoint**: Import is verified to be streaming and memory-safe. No code changes expected if T017/T018 were implemented correctly — this is a verification/hardening task.

---

## Phase 6: User Story 4 — Identity Resolution During Import (Priority: P2)

**Goal**: Map source user identities to target identities during field application, using `IIdentityMappingService`.

**Independent Test**: Import a package with known source identities. Verify identity fields (`System.AssignedTo`, `System.ChangedBy`) are mapped to target identities. Verify unresolved identities are logged.

### Gherkin Feature File for User Story 4 (mandatory)

- [x] T030 [US4] Create `features/services/identity-mapping/import-identity-resolution.feature` — translate spec.md User Story 4 acceptance scenarios 1–3 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Identity Resolution During Import)

### Implementation for User Story 4

- [x] T031 [US4] Add identity resolution to `RevisionFolderProcessor` Stage B in `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionFolderProcessor.cs` — before applying fields, iterate identity-type fields (e.g. `System.AssignedTo`, `System.ChangedBy`, `System.CreatedBy`) and call `IIdentityMappingService.ResolveAsync()` to map source identity → target identity. Replace field values with resolved identities. If unresolved, log to `Identities/unresolved.json` via `IArtefactStore.WriteAsync` and continue (do not fail).

**Checkpoint**: Identity fields are mapped during import. Unresolved identities are logged without halting.

---

## Phase 7: User Story 5 — Comment Import (Priority: P2)

**Goal**: Import comment sub-folders and inline `comment.json` arrays into the target via the Comments API.

**Independent Test**: Import a package with comment sub-folders (`<ticks>-<workItemId>-c<commentId>/`) and inline `comment.json` files. Verify comments appear on the target work items.

### Gherkin Feature File for User Story 5 (mandatory)

- [x] T032 [US5] Create `features/import/work-items/comments/import-comments.feature` — translate spec.md User Story 5 acceptance scenarios 1–3 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Import Work Item Comments)

### Implementation for User Story 5

- [x] T033 [US5] Add comment sub-folder processing to `WorkItemImportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — when the folder name's third segment starts with `c` (e.g. `c42`), read `comment.json` (single JSON object) from the folder via `IArtefactStore.ReadAsync`, deserialise to `WorkItemComment`, resolve target work item ID from `IIdMapStore`, call `IWorkItemImportTarget.CreateCommentAsync`. Write cursor with `Completed` stage after comment creation. Skip if Comments extension is disabled.
- [x] T034 [US5] Add inline comment processing to `RevisionFolderProcessor` in `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionFolderProcessor.cs` — after Stage D (UploadedAttachments), check if `comment.json` exists in the revision folder via `IArtefactStore`. If present, read and deserialise as a JSON array of `WorkItemComment` objects, iterate each, call `IWorkItemImportTarget.CreateCommentAsync` for each. Skip if Comments extension is disabled.

**Checkpoint**: Both standalone comment folders and inline comments are imported. Comments appear on the correct target work items.

---

## Phase 8: User Story 6 — Embedded Image URL Rewriting (Priority: P3)

**Goal**: Upload embedded images to the target and rewrite source URLs in field values and comment text.

**Independent Test**: Import revision folders with `embeddedImages` metadata. Verify images are uploaded to the target and field HTML contains new target URLs.

### Gherkin Feature File for User Story 6 (mandatory)

- [x] T035 [US6] Create `features/import/work-items/revisions/import-embedded-images.feature` — translate spec.md User Story 6 acceptance scenarios 1–2 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Import Embedded Image URL Rewriting)

### Implementation for User Story 6

- [x] T036 [US6] Add embedded image upload and URL rewriting to `RevisionFolderProcessor` in `src/DevOpsMigrationPlatform.Infrastructure/Import/RevisionFolderProcessor.cs` — before Stage B (AppliedFields), if EmbeddedImages extension is enabled and `revision.embeddedImages` is non-empty: for each entry, read the image binary from the revision folder via `IArtefactStore.ReadBinaryAsync(folderPath + "/" + relativePath)`, upload via `IWorkItemImportTarget.UploadEmbeddedImageAsync`, build URL map (`originalUrl → targetUrl`). Then scan all field values for `originalUrl` occurrences and replace with `targetUrl` before applying fields.
- [x] T037 [US6] Add embedded image URL rewriting to comment text in `RevisionFolderProcessor` and `WorkItemImportOrchestrator` — when processing inline comments or comment sub-folders with `embeddedImages` entries: upload images, rewrite URLs in comment `text` before calling `CreateCommentAsync`. Skip if EmbeddedImages extension is disabled.

**Checkpoint**: Embedded images are uploaded and URLs in field values and comments are rewritten to target URLs.

---

## Phase 9: User Story 1 (continued) — Work Item Resolution Strategies (Priority: P1)

**Goal**: Implement the `TargetField` and `TargetHyperlink` resolution strategies for seeding `idmap.db` from the target.

**Independent Test**: Configure `WorkItemResolutionStrategy: TargetField` with a custom field. Run import. Verify startup WIQL query seeds `idmap.db` and existing target work items are reused (no duplicates). Repeat for `TargetHyperlink` with a URL pattern.

### Gherkin Feature File for Resolution Strategies (mandatory)

- [x] T038 [US1] Create `features/import/work-items/revisions/work-item-resolution-strategies.feature` — translate spec.md User Story 1 acceptance scenarios 7–8 into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md` (Feature: Work Item Resolution Strategies)

### Implementation for Resolution Strategies

- [x] T039 [US1] Implement `TargetFieldResolutionStrategy` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/TargetFieldResolutionStrategy.cs` — constructor receives `IWorkItemImportTarget` (or WIQL client) and the configured field name. `SeedAsync`: WIQL query `SELECT [System.Id], [<fieldName>] FROM WorkItems WHERE [<fieldName>] <> ''` → parse results → call `IIdMapStore.SeedWorkItemMappingsAsync`. `ResolveSingleAsync`: WIQL query `SELECT [System.Id] FROM WorkItems WHERE [<fieldName>] = '<sourceId>'` → return target ID or null. `WriteProvenanceAsync`: update target work item custom field with source ID via `IWorkItemImportTarget.UpdateFieldsAsync`.
- [x] T040 [US1] Implement `TargetHyperlinkResolutionStrategy` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/TargetHyperlinkResolutionStrategy.cs` — constructor receives `IWorkItemImportTarget` and the configured URL pattern. `SeedAsync`: WIQL query for `[System.HyperLinkCount] > 0` → fetch each work item's relations via `GetExistingRelationsAsync` → filter hyperlinks matching URL pattern → extract source work item ID from URL → seed `IIdMapStore`. `ResolveSingleAsync`: return null (no live fallback per FR-022). `WriteProvenanceAsync`: add hyperlink with URL pattern containing source ID via `IWorkItemImportTarget.AddLinksAsync`.
- [x] T041 [US1] Wire resolution strategy selection in `ImportServiceCollectionExtensions` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ImportServiceCollectionExtensions.cs` — read `WorkItemResolutionStrategy` extension from module config. If `strategy: TargetField`, register `TargetFieldResolutionStrategy`. If `strategy: TargetHyperlink`, register `TargetHyperlinkResolutionStrategy`. If absent or disabled, register `NullResolutionStrategy` (already created in T015).

**Checkpoint**: All three resolution strategies work. `TargetField` seeds and live-queries. `TargetHyperlink` seeds at startup with no live fallback. Null strategy uses `idmap.db` only.

---

## Phase 10: Documentation Sync (MANDATORY)

**Purpose**: Update all canonical docs to reflect what was implemented. This phase is a blocking gate.

- [x] T042 Update `docs/work-item-iteration-guide.md` — add "Import Pattern: WorkItemImportOrchestrator" section with `IWorkItemImportTarget`, `IIdMapStore`, `RevisionFolderProcessor`, and streaming import flow (mirrors existing "Export Pattern" section). Resolves discrepancy #3 and #7.
- [x] T043 [P] Update `docs/configuration-reference.md` — add `WorkItemResolutionStrategy` extension type to the WorkItems Module Scopes and Extensions table with `strategy` (`TargetField` | `TargetHyperlink`), `fieldName`, and `urlPattern` parameters. Resolves discrepancy #6.
- [x] T044 [P] Update `.agents/30-context/domains/cli-commands.md` — confirm `queue` command documentation reflects that `Import` and `Both` modes are now functional (no longer stubbed). No new commands added.
- [x] T045 [P] Update `docs/cli-guide.md` — confirm import mode is documented as functional. No new commands.
- [x] T046 Mark all items in `specs/013-ado-workitems-import/discrepancies.md` as `Resolved` or `N/A`
- [x] T047 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [x] T048 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [x] T049 Run `dotnet test` — ALL tests MUST pass
- [x] T050 Run scenario config `scenarios/import-ado-workitems-single-project.json` via the `.vscode/launch.json` import debug profile and verify observable output (work items created in simulated target, cursor written, idmap populated, progress events emitted)
- [x] T051 Add `ValidateAsync` implementation to `WorkItemsModule` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — Tier 2 pre-flight: verify `WorkItems/` folder exists, at least one revision folder, `manifest.json` compatible. Tier 3 post-flight: target work item count matches unique source IDs, sampled link/attachment verification. Required by SC-004.

---

## Phase 11: Polish & Cross-Cutting Concerns (OPTIONAL)

**Purpose**: Improvements that span multiple user stories.

- [ ] T052 [P] Add logging and metrics instrumentation to import orchestrator and processor — structured log entries for each stage transition, OpenTelemetry counters for work items created/updated/skipped, histogram for per-folder processing time.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001 for SQLite package) — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2) — this is the MVP
- **User Story 2 (Phase 4)**: Depends on Phase 3 (T017, T018 — extends the processor and orchestrator)
- **User Story 3 (Phase 5)**: Depends on Phase 3 (verification of T017, T018 streaming compliance)
- **User Story 4 (Phase 6)**: Depends on Phase 3 (T017 — extends RevisionFolderProcessor Stage B)
- **User Story 5 (Phase 7)**: Depends on Phase 3 (T018 — extends orchestrator and processor)
- **User Story 6 (Phase 8)**: Depends on Phase 3 (T017 — extends RevisionFolderProcessor) and Phase 7 (comment URL rewriting depends on comment import)
- **Resolution Strategies (Phase 9)**: Depends on Phase 3 (T018 orchestrator, T014 SqliteIdMapStore)
- **Documentation Sync (Phase 10)**: Depends on all implementation phases — blocking gate
- **Polish (Phase 11)**: Depends on all desired phases being complete

### User Story Dependencies

- **US1 (Phase 3 + Phase 9)**: Core MVP — can start after Phase 2. Resolution strategies (Phase 9) can run in parallel with US2–US6 once Phase 3 is complete.
- **US2 (Phase 4)**: Extends Phase 3 code. Can start after Phase 3 checkpoint.
- **US3 (Phase 5)**: Verifies Phase 3 code. Can start after Phase 3 checkpoint. Parallelisable with US2.
- **US4 (Phase 6)**: Extends Phase 3 code. Can start after Phase 3 checkpoint. Parallelisable with US2, US3.
- **US5 (Phase 7)**: Extends Phase 3 code. Can start after Phase 3 checkpoint. Parallelisable with US2, US3, US4.
- **US6 (Phase 8)**: Depends on Phase 3 and Phase 7 (comment image rewriting). Start after Phase 7.

### Within Each User Story

- Gherkin `.feature` file MUST be written first (ATDD Phase 1 artifact)
- Models before services
- Services before endpoints
- Story complete before moving to next priority

### Parallel Opportunities

**After Phase 2 completes (Foundational):**
- T004–T015 are all parallelisable where marked [P]

**After Phase 3 completes (US1 MVP):**
- US2 (Phase 4), US3 (Phase 5), US4 (Phase 6), US5 (Phase 7) can all start in parallel
- Phase 9 (Resolution Strategies) can start in parallel with US2–US5

**Within Phase 10 (Doc Sync):**
- T042, T043, T044, T045 are parallelisable where marked [P]

---

## Parallel Example: After Phase 3 Checkpoint

```
Developer A: Phase 4 (US2 — Cursor Resume)     ← extends processor/orchestrator
Developer B: Phase 6 (US4 — Identity Resolution) ← extends processor Stage B
Developer C: Phase 9 (Resolution Strategies)      ← new strategy classes
Developer D: Phase 7 (US5 — Comment Import)       ← extends orchestrator/processor
```

All four work on different files/methods and can proceed independently.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T015) — all interfaces and models
3. Complete Phase 3: User Story 1 (T016–T024) — core import end-to-end
4. **STOP and VALIDATE**: Run `scenarios/import-ado-workitems-single-project.json` and verify work items created, cursor written, idmap populated
5. This is a deployable MVP

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 (Phase 3) → Test independently → **MVP** 🎯
3. US2 (Phase 4) → Resume works → Deploy
4. US3 (Phase 5) → Memory safety verified → Deploy
5. US4 (Phase 6) → Identity mapping → Deploy
6. US5 (Phase 7) → Comments imported → Deploy
7. US6 (Phase 8) → Embedded images → Deploy
8. Resolution Strategies (Phase 9) → Repeat imports → Deploy
9. Doc Sync (Phase 10) → MANDATORY gate → Feature complete
10. Polish (Phase 11) → Validation and edge cases → Ship

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps tasks to specific user stories for traceability
- Each user story is independently completable and testable after Phase 3 MVP
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently

