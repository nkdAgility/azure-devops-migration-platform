---

description: "Task list template for feature implementation"
---

# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Test tasks for business logic are OPTIONAL — only include them if explicitly requested in the feature specification. Observability tests (O-1 through O-4) are MANDATORY in every user story phase, no exceptions.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Single project**: `src/`, `tests/` at repository root
- **Web app**: `backend/src/`, `frontend/src/`
- **Mobile**: `api/src/`, `ios/src/` or `android/src/`
- Paths shown below assume single project - adjust based on plan.md structure

<!-- 
  ============================================================================
  IMPORTANT: The tasks below are SAMPLE TASKS for illustration purposes only.
  
  The /speckit.tasks command MUST replace these with actual tasks based on:
  - User stories from spec.md (with their priorities P1, P2, P3...)
  - Feature requirements from plan.md
  - Entities from data-model.md
  - Endpoints from contracts/
  
  Tasks MUST be organized by user story so each story can be:
  - Implemented independently
  - Tested independently
  - Delivered as an MVP increment
  
  DO NOT keep these sample tasks in the generated tasks.md file.
  ============================================================================
-->

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create project structure per implementation plan
- [ ] T002 Initialize [language] project with [framework] dependencies
- [ ] T003 [P] Configure linting and formatting tools

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

Examples of foundational tasks (adjust based on your project):

- [ ] T004 Setup database schema and migrations framework
- [ ] T005 [P] Implement authentication/authorization framework
- [ ] T006 [P] Setup API routing and middleware structure
- [ ] T007 Create base models/entities that all stories depend on
- [ ] T008 Configure error handling and logging infrastructure
- [ ] T009 Setup environment configuration management
- [ ] T010 [P] [US1] Create `features/<tier>/<module>/<story>.feature` — Gherkin scenarios translating the `spec.md` User Story 1 acceptance scenarios into conformant `.feature` format (see `.agents/guardrails/acceptance-test-format.md` for tier/naming rules). This file is the ATDD Phase 1 input for User Story 1 and must exist before any step definitions are written.

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - [Title] (Priority: P1) 🎯 MVP

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Gherkin Feature File for User Story 1 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 1 acceptance scenarios and committed before any step definitions or production code are written.**

- [ ] T010 [US1] Create `features/<tier>/<module>/<story-1>.feature` — translate `spec.md` User Story 1 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Tests for User Story 1 (OPTIONAL - only if tests requested) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T011 [P] [US1] Contract test for [endpoint] in tests/contract/test_[name].py
- [ ] T012 [P] [US1] Integration test for [user journey] in tests/integration/test_[name].py

### Implementation for User Story 1

- [ ] T012 [P] [US1] Create [Entity1] model in src/models/[entity1].py
- [ ] T013 [P] [US1] Create [Entity2] model in src/models/[entity2].py
- [ ] T014 [US1] Implement [Service] in src/services/[service].py (depends on T012, T013)
- [ ] T015 [US1] Implement [endpoint/feature] in src/[location]/[file].py
- [ ] T016 [US1] Add validation and error handling

### Observability for User Story 1 ⛔ MANDATORY — zero exceptions

> These tasks are not optional. They are not "nice to have". Every item below MUST be implemented and verified before the checkpoint is reached. Reference the Operations Table in `plan.md ## Observability Contract` for the exact span names, metric instruments, log events, and ProgressEvent stages.

- [ ] T017 [US1] **O-1 Traces** — Add `using var activity = ActivitySource.StartActivity("[span-name]")` to every operation in [Service/Module], with tags per `WellKnownTagNames` (jobId, module, connector at minimum)
- [ ] T018 [US1] **O-2 Metrics** — Call `IMigrationMetrics.RecordAttempt(tags)`, `RecordCompleted(tags)`, `RecordError(tags)`, `RecordDuration(elapsed, tags)`, and `RecordInFlight(+1/-1, tags)` at every operation boundary in [Service/Module]
- [ ] T019 [US1] **O-3 Logs** — Add `_logger.LogInformation("Starting [Operation] for {Count} items", count)` at operation start; `_logger.LogInformation("Completed [Operation]: {Processed} processed, {Skipped} skipped", ...)` at end; `_logger.LogWarning("Skipping {Id}: {Reason}", ...)` for skip paths; `_logger.LogDebug("Processing {Path}", ...)` per-item
- [ ] T020 [US1] **O-4 ProgressEvents** — Inject `IProgressSink?` as optional constructor parameter; call `EmitAsync(new ProgressEvent { Module = Name, Stage = "Exporting", ... })` at start; call per-item (or per batch ≤50); call at completion with final counts; populate `Metrics.Migration.{ModuleName}` with `ModuleCounters`
- [ ] T021 [US1] **O-4 CLI Visible** — Add/verify progress bar row for this module in `QueueCommand.BuildProgressRenderable` in correct execution order; verify row is visible when running a scenario config
- [ ] T022 [US1] **DI Wiring** — Verify every new class implementing an interface has a `services.AddSingleton<IFoo, Foo>()` (or correct lifetime) in the feature's `Add*Services` extension method; verify that extension method is called from the host startup
- [ ] T023 [P] [US1] **Test O-1** — Unit test: inject `TestActivityListener`; call [Service/Module] method; assert `StartActivity` was called with span name `"[span-name]"` and required tags
- [ ] T024 [P] [US1] **Test O-2** — Unit test: inject `Mock<IMigrationMetrics>`; call [Service/Module] method; assert `RecordAttempt`, `RecordCompleted`, and `RecordDuration` called with correct `TagList`
- [ ] T025 [P] [US1] **Test O-4** — Unit test: inject `Mock<IProgressSink>`; call [Service/Module] operation; assert `EmitAsync` called at start, once per item (or per batch ≤50), and at completion; assert `Metrics.Migration.{ModuleName}` is populated

**Checkpoint**: At this point, User Story 1 is fully functional, fully observable, and has passing tests for all four observability requirements. No placeholder calls, no null sinks, no missing CLI rows.

---

## Phase 4: User Story 2 - [Title] (Priority: P2)

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Gherkin Feature File for User Story 2 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 2 acceptance scenarios before any step definitions or production code.**

- [ ] T018 [US2] Create `features/<tier>/<module>/<story-2>.feature` — translate `spec.md` User Story 2 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Tests for User Story 2 (OPTIONAL - only if tests requested) ⚠️

- [ ] T019 [P] [US2] Contract test for [endpoint] in tests/contract/test_[name].py
- [ ] T020 [P] [US2] Integration test for [user journey] in tests/integration/test_[name].py

### Implementation for User Story 2

- [ ] T020 [P] [US2] Create [Entity] model in src/models/[entity].py
- [ ] T021 [US2] Implement [Service] in src/services/[service].py
- [ ] T022 [US2] Implement [endpoint/feature] in src/[location]/[file].py
- [ ] T023 [US2] Integrate with User Story 1 components (if needed)

### Observability for User Story 2 ⛔ MANDATORY — zero exceptions

> Reference the Operations Table in `plan.md ## Observability Contract` for exact names.

- [ ] TXXX [US2] **O-1 Traces** — `StartActivity("[span-name]")` with required tags on every operation in [Service/Module]
- [ ] TXXX [US2] **O-2 Metrics** — `IMigrationMetrics` attempt/completed/error/duration/in-flight calls at every operation boundary
- [ ] TXXX [US2] **O-3 Logs** — `LogInformation` at start/end with counts; `LogWarning` on skips/errors; `LogDebug` per-item with structured params
- [ ] TXXX [US2] **O-4 ProgressEvents** — `IProgressSink?` injected; `EmitAsync` called at start, per-item (≤50 batch), and completion; `Metrics.Migration.{ModuleName}` populated
- [ ] TXXX [US2] **O-4 CLI Visible** — Progress bar row present in `QueueCommand.BuildProgressRenderable` in correct execution order
- [ ] TXXX [US2] **DI Wiring** — All new classes registered in `Add*Services` extension; extension called from host startup
- [ ] TXXX [P] [US2] **Test O-1/O-2/O-4** — Unit tests asserting `StartActivity`, `IMigrationMetrics`, and `IProgressSink.EmitAsync` called correctly

**Checkpoint**: At this point, User Stories 1 AND 2 are both fully functional, fully observable, and independently testable.

---

## Phase 5: User Story 3 - [Title] (Priority: P3)

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Gherkin Feature File for User Story 3 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 3 acceptance scenarios before any step definitions or production code.**

- [ ] T024 [US3] Create `features/<tier>/<module>/<story-3>.feature` — translate `spec.md` User Story 3 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Tests for User Story 3 (OPTIONAL - only if tests requested) ⚠️

- [ ] T025 [P] [US3] Contract test for [endpoint] in tests/contract/test_[name].py
- [ ] T026 [P] [US3] Integration test for [user journey] in tests/integration/test_[name].py

### Implementation for User Story 3

- [ ] T026 [P] [US3] Create [Entity] model in src/models/[entity].py
- [ ] T027 [US3] Implement [Service] in src/services/[service].py
- [ ] T028 [US3] Implement [endpoint/feature] in src/[location]/[file].py

### Observability for User Story 3 ⛔ MANDATORY — zero exceptions

> Reference the Operations Table in `plan.md ## Observability Contract` for exact names.

- [ ] TXXX [US3] **O-1 Traces** — `StartActivity("[span-name]")` with required tags on every operation
- [ ] TXXX [US3] **O-2 Metrics** — `IMigrationMetrics` attempt/completed/error/duration/in-flight calls at every operation boundary
- [ ] TXXX [US3] **O-3 Logs** — `LogInformation` at start/end with counts; `LogWarning` on skips/errors; `LogDebug` per-item with structured params
- [ ] TXXX [US3] **O-4 ProgressEvents** — `IProgressSink?` injected; `EmitAsync` at start, per-item (≤50 batch), and completion; `Metrics.Migration.{ModuleName}` populated
- [ ] TXXX [US3] **O-4 CLI Visible** — Progress bar row in `QueueCommand.BuildProgressRenderable` in correct order
- [ ] TXXX [US3] **DI Wiring** — All new classes registered in `Add*Services`; extension called from host startup
- [ ] TXXX [P] [US3] **Test O-1/O-2/O-4** — Unit tests asserting `StartActivity`, `IMigrationMetrics`, and `IProgressSink.EmitAsync` called correctly

**Checkpoint**: All user stories are independently functional and fully observable.

---

[Add more user story phases as needed, following the same pattern]

---

## Phase N: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect what was implemented in this spec. This phase is a blocking gate — no spec is complete without it. The doc-update tasks below MUST reference the exact file path and section being updated. Vague tasks like "update docs" are not accepted.

- [ ] TXXX Update `docs/<primary-doc>.md` — add/update section for `<feature>` per `discrepancies.md` items (exact section: `<heading>`)
- [ ] TXXX [P] Update `.agents/context/<context-file>.md` — align with implementation changes (see `discrepancies.md`)
- [ ] TXXX Mark all items in `specs/<feature>/discrepancies.md` as `Resolved` or `N/A`
- [ ] TXXX Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [ ] TXXX Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] TXXX Run `dotnet test` — ALL tests MUST pass
- [ ] TXXX Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

> **If none of the above canonical docs were affected by this spec**, the agent MUST explicitly state: "No documentation changes required — `<brief justification>`". This statement must appear in the session log `doc_sync.no_change_justification` field. Silence is not acceptable.

## Phase N+1: Polish & Cross-Cutting Concerns (OPTIONAL)

**Purpose**: Improvements that affect multiple user stories

- [ ] TXXX Code cleanup and refactoring
- [ ] TXXX Performance optimization across all stories
- [ ] TXXX [P] Additional unit tests (if requested) in tests/unit/
- [ ] TXXX Security hardening

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation
- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Models within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together (if tests requested):
Task: "Contract test for [endpoint] in tests/contract/test_[name].py"
Task: "Integration test for [user journey] in tests/integration/test_[name].py"

# Launch all models for User Story 1 together:
Task: "Create [Entity1] model in src/models/[entity1].py"
Task: "Create [Entity2] model in src/models/[entity2].py"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2
   - Developer C: User Story 3
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence

## Observability Rules (Non-Negotiable)

These rules apply to every generated `tasks.md` in this project. They are not suggestions.

1. **Every user story phase MUST contain the full O-1 through O-4 + DI wiring task block.** There are no exceptions. A user story that touches production code but has no observability tasks is invalid and must not be marked complete.
2. **Observability tasks are not "nice to have" additions at the end.** They are part of the definition of done for each story and must be implemented before the story checkpoint is reached.
3. **The Operations Table in `plan.md ## Observability Contract` is the authoritative source** for span names, metric instruments, log events, and ProgressEvent stages. `tasks.md` tasks must reference those exact names — not placeholders.
4. **O-4 CLI Visible is always required.** If a new module counter property is added to `MigrationCounters`, the corresponding `QueueCommand.BuildProgressRenderable` row MUST be added in the same task. These two changes are atomic.
5. **DI wiring MUST be verified as a task.** It is not sufficient for a class to exist — it must be registered. Verify the `Add*Services` call chain from class → extension method → host startup.
6. **Observability tests are mandatory.** At minimum: one test each for O-1 (span emitted), O-2 (metrics recorded), and O-4 (ProgressEvent emitted at start and completion). These are unit tests using mocks — they must not require a live connection.
