# Tasks: Work Item Scoped Fetch Service

**Input**: Design documents from `/specs/015-work-item-scoped-fetch/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/IWorkItemFetchService.md

**Tests**: Unit tests are required — the spec mandates testable behaviour, the plan specifies Reqnroll.MSTest + Moq, and FR-004 (filter evaluation) has branching logic requiring coverage.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new types in Abstractions that all user stories depend on

- [ ] T001 [P] Create `FilterOperator` enum in `src/DevOpsMigrationPlatform.Abstractions/Models/FilterOperator.cs` — `Equals`, `NotEquals`, `Contains` values (placeholder for feature 014, see data-model.md)
- [ ] T002 [P] Create `WorkItemFieldFilterOptions` immutable record in `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemFieldFilterOptions.cs` — `FieldName` (string), `Operator` (FilterOperator), `Value` (object?) properties (placeholder for feature 014, see data-model.md)
- [ ] T003 [P] Create `FetchedWorkItem` immutable record in `src/DevOpsMigrationPlatform.Abstractions/Models/FetchedWorkItem.cs` — `int Id` + `IReadOnlyDictionary<string, object?> Fields` (FR-006, see data-model.md)
- [ ] T004 Create `WorkItemFetchScope` immutable record in `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemFetchScope.cs` — `Fields` (IReadOnlyList\<string\>), `FilterOptions` (IReadOnlyList\<WorkItemFieldFilterOptions\>?), `BaseQuery` (string?) (depends on T001, T002; see data-model.md)
- [ ] T005 Create `IWorkItemFetchService` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemFetchService.cs` — `FetchAsync(OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken)` returning `IAsyncEnumerable<FetchedWorkItem>` (depends on T003, T004; FR-001, FR-002, FR-003; see contracts/IWorkItemFetchService.md)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: ADO implementation of the fetch service — MUST be complete before caller refactoring can begin

**⚠️ CRITICAL**: No caller refactoring (US1, US2) can begin until T006 is complete and registered in DI

- [ ] T006 Implement `AzureDevOpsWorkItemFetchService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs` — constructor injects `IWorkItemQueryWindowStrategy` + `IAzureDevOpsClientFactory`; `FetchAsync` enumerates windows, batch-fetches (200 IDs) with field projection via `GetWorkItemsAsync(ids, fields:)`, evaluates `FilterOptions` in-process (AND semantics), yields `FetchedWorkItem` one at a time; validates `scope.Fields` not empty (ArgumentException); propagates CancellationToken throughout (FR-003, FR-004, FR-005, FR-007, FR-013, FR-014; see research.md R-003, R-004, R-006)
- [ ] T007 Register `IWorkItemFetchService` → `AzureDevOpsWorkItemFetchService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/InventoryServiceCollectionExtensions.cs` — add `services.AddSingleton<IWorkItemFetchService, AzureDevOpsWorkItemFetchService>()` with conditional check to prevent double-registration (follows existing pattern; depends on T006)
- [ ] T008 Register `IWorkItemFetchService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs` — add conditional `IWorkItemFetchService` registration matching the pattern in T007 (depends on T006)
- [ ] T009 [P] Create unit tests for filter evaluation logic in `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Services/WorkItemFieldFilterEvaluatorTests.cs` — test Equals (case-insensitive string), NotEquals, Contains operators; test null FilterOptions passthrough; test AND semantics across multiple filters; test missing field on work item (should not match Equals); test null Value handling (MSTest [TestClass]/[TestMethod], Moq MockBehavior.Strict)
- [ ] T010 Create unit tests for `AzureDevOpsWorkItemFetchService` in `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs` — test streaming (mock window strategy returns 2 windows, verify items yielded per-batch not all-at-once); test empty fields throws ArgumentException; test empty window returns empty sequence; test CancellationToken propagation; test field projection passed to GetWorkItemsAsync; test transient API exception mid-stream propagates to caller without partial buffering; test missing field on work item type (field omitted from result dictionary, not an error); test zero-ID baseQuery returns empty sequence with no batch calls (depends on T006, T009)

**Checkpoint**: Foundation ready — `IWorkItemFetchService` is implemented, registered, and tested. Caller refactoring can begin.

---

## Phase 3: User Story 1 — Inventory counts work items with field projection and filters (Priority: P1) 🎯 MVP

**Goal**: Refactor `AzureDevOpsWorkItemDiscoveryService` to delegate its inner batch loop to `IWorkItemFetchService` instead of calling `GetWorkItemsAsync` directly (FR-009)

**Independent Test**: Run `devopsmigration discovery inventory` against a project and verify that (a) only declared fields appear in fetch requests, and (b) results stream without buffering the full set.

### Gherkin Feature File for User Story 1 (mandatory)

- [ ] T011 [US1] Create `features/inventory/work-items/inventory-field-projection.feature` — translate spec.md US1 acceptance scenarios (field projection, type filter, bounded memory) into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 1

- [ ] T012 [US1] Refactor `AzureDevOpsWorkItemDiscoveryService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemDiscoveryService.cs` — replace direct `witClient.GetWorkItemsAsync(batch, fields: ["System.Rev"])` batch loop with `IWorkItemFetchService.FetchAsync(endpoint, project, scope, ct)`; inject `IWorkItemFetchService` via constructor; remove `IAzureDevOpsClientFactory` dependency if no longer needed for field fetching; keep `IWorkItemQueryWindowStrategy` only if still needed for counting path (FR-009, FR-014; see research.md R-007)
- [ ] T013 [US1] Update unit tests for `AzureDevOpsWorkItemDiscoveryService` in `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Services/` — mock `IWorkItemFetchService` instead of `IAzureDevOpsClientFactory` for field-fetch paths; verify CancellationToken is forwarded; verify scope contains expected fields
- [ ] T014 [US1] Verify `CatalogService` in `src/DevOpsMigrationPlatform.Infrastructure/Services/CatalogService.cs` still works transitively — no logic change expected; confirm passthrough delegation pattern is unaffected by discovery refactor (FR-011)

**Checkpoint**: Inventory path uses `IWorkItemFetchService`. `CatalogService` works transitively. SC-002 partially satisfied (discovery has zero direct `GetWorkItemsAsync` calls).

---

## Phase 4: User Story 2 — Dependency analysis pre-filters by field before fetching relations (Priority: P2)

**Goal**: Refactor `AzureDevOpsDependencyAnalysisService` to use `IWorkItemFetchService` for field-based pre-filtering; Relations expansion remains a separate call performed only for items that pass the filter (FR-010)

**Independent Test**: Run a dependency scan on a project where only 10% of work items match the type filter, and confirm Relations calls are made only for matching items.

### Gherkin Feature File for User Story 2 (mandatory)

- [ ] T015 [US2] Create `features/inventory/work-items/dependency-pre-filter.feature` — translate spec.md US2 acceptance scenarios (pre-filter before Relations expand, non-matching items not yielded, caller-owned relation expansion) into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 2

- [ ] T016 [US2] Refactor `AzureDevOpsDependencyAnalysisService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs` — inject `IWorkItemFetchService` via constructor; replace the two-phase pattern (collect all IDs → batch fetch with Relations) with: Phase 1 uses `IWorkItemFetchService.FetchAsync` to stream pre-filtered items, Phase 2 fetches Relations only for items that passed the filter; keep `IAzureDevOpsClientFactory` for the Relations expansion call (`GetWorkItemAsync(id, expand: WorkItemExpand.Relations)`); remove direct `GetWorkItemsAsync` calls for field fetching; progress reporting switches from total-item-count percentage to window-count progress (e.g. "Processing window 3 of N") since total IDs are no longer collected in memory (FR-010, FR-013, FR-014; see research.md R-007)
- [ ] T017 [US2] Update unit tests for `AzureDevOpsDependencyAnalysisService` in `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Services/` — mock `IWorkItemFetchService` to return pre-filtered items; verify Relations expand calls are made only for yielded items; verify non-matching items do not trigger Relations fetches; verify `IAzureDevOpsClientFactory` is still used for Relations expansion

**Checkpoint**: Dependency analysis uses `IWorkItemFetchService` for pre-filtering. Relations expand only called for matching items. SC-002 fully satisfied (both callers have zero direct `GetWorkItemsAsync` calls for field fetching).

---

## Phase 5: User Story 3 — TFS source returns field-projected work items via the same interface (Priority: P3)

**Goal**: Implement a functional TFS-backed `IWorkItemFetchService` so that inventory and dependency analysis work uniformly regardless of source type (FR-008)

**Independent Test**: Run inventory against a TFS source and confirm the same interface contract is exercised.

### Gherkin Feature File for User Story 3 (mandatory)

- [ ] T018 [US3] Create `features/inventory/work-items/tfs-field-projection.feature` — translate spec.md US3 acceptance scenarios (TFS source streams items with requested fields, filter exclusion works on TFS) into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 3

- [ ] T019 [US3] Implement `TfsWorkItemFetchService` in `src/DevOpsMigrationPlatform.CLI.TfsExport/Services/TfsWorkItemFetchService.cs` — net481-only project (TFS Object Model types cannot compile under net10.0); functional implementation using TFS Object Model `WorkItemStore.Query()` with field-projected WIQL SELECT; evaluates `FilterOptions` in-process (same AND semantics as ADO implementation); streams results via `IAsyncEnumerable<FetchedWorkItem>`; validates `scope.Fields` not empty (ArgumentException); must NOT throw `NotImplementedException` (FR-008; see research.md R-008)
- [ ] T020 [US3] Register `TfsWorkItemFetchService` in the appropriate TFS DI extension method within `src/DevOpsMigrationPlatform.CLI.TfsExport` — conditional registration matching the pattern used for ADO services (depends on T019)
- [ ] T021 [US3] Create unit tests for `TfsWorkItemFetchService` in `tests/DevOpsMigrationPlatform.CLI.TfsExport.Tests/Services/TfsWorkItemFetchServiceTests.cs` — net481-only test project; test field projection via mock WorkItemStore; test filter evaluation; test empty fields throws ArgumentException; test empty query returns empty sequence (MSTest [TestClass]/[TestMethod], Moq MockBehavior.Strict)

**Checkpoint**: TFS implementation is functional and tested. SC-005 satisfied (both ADO and TFS exercise the same `IWorkItemFetchService` interface contract).

---

## Phase 6: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect what was implemented in this spec. Resolves all discrepancies from `discrepancies.md`.

- [ ] T022 Update `docs/work-item-iteration-pattern.md` — add rule 6 under "Mandatory Reuse Principle" section: "Use `IWorkItemFetchService` for field-projected, filtered work item fetching in inventory, dependency analysis, and catalog operations. Do not call `GetWorkItemsAsync` directly from these callers." Also add `IWorkItemFetchService` and `FetchedWorkItem` to the Overview section abstractions list (resolves D-001)
- [ ] T023 [P] Update `docs/modules.md` — add note to Module Responsibilities section referencing `IWorkItemFetchService` for inventory/dependency modules (resolves D-002)
- [ ] T024 [P] Update `docs/architecture.md` — add `IWorkItemFetchService` to the OrganisationEndpoint section as a consumer of `OrganisationEndpoint` as connection context (resolves D-003)
- [ ] T025 Mark all items in `specs/015-work-item-scoped-fetch/discrepancies.md` as `Resolved` or `N/A` — D-001 (Resolved by T022), D-002 (Resolved by T023), D-003 (Resolved by T024), D-004 (N/A — deferred to future feature)
- [ ] T026 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [ ] T027 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T028 Run `dotnet test` — ALL tests MUST pass
- [ ] T029 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

---

## Phase 7: Polish & Cross-Cutting Concerns (OPTIONAL)

**Purpose**: Post-delivery improvements

- [ ] T030 [P] Extract filter evaluation logic into a shared static helper class if both ADO and TFS implementations duplicate it — DRY principle; only if duplication is observed after US3 implementation
- [ ] T031 [P] Add XML doc-comments to all new public types in Abstractions (`IWorkItemFetchService`, `WorkItemFetchScope`, `FetchedWorkItem`, `WorkItemFieldFilterOptions`, `FilterOperator`) per coding standard 21

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion (T001–T005) — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 completion (T006–T010)
- **User Story 2 (Phase 4)**: Depends on Phase 2 completion (T006–T010); independent of US1
- **User Story 3 (Phase 5)**: Depends on Phase 1 completion (T001–T005); independent of US1 and US2
- **Documentation Sync (Phase 6)**: Depends on Phases 3–5 completion
- **Polish (Phase 7)**: Depends on Phase 6 completion

### User Story Dependencies

- **User Story 1 (P1)**: Depends on Foundational (Phase 2) — no dependency on US2 or US3
- **User Story 2 (P2)**: Depends on Foundational (Phase 2) — no dependency on US1 or US3; can run in parallel with US1
- **User Story 3 (P3)**: Depends on Setup (Phase 1) only — the TFS implementation only needs the Abstractions types, not the ADO implementation; can run in parallel with US1 and US2

### Within Each User Story

- Gherkin feature file MUST be written before implementation
- Models/types before services
- Services before caller refactoring
- Core implementation before integration/tests
- Story complete before Documentation Sync phase

### Parallel Opportunities

- **Phase 1**: T001, T002, T003 can all run in parallel (independent files)
- **Phase 2**: T009 (filter tests) can run in parallel with T006 (implementation) if TDD; T007, T008 (DI registration) can run in parallel
- **Phase 3 + Phase 4**: US1 and US2 can run in parallel (they refactor different service classes)
- **Phase 5**: US3 can run in parallel with US1 and US2 (different project, different files)
- **Phase 7**: T030, T031 can run in parallel

---

## Parallel Example: After Foundational Phase

```
# All three user stories can start simultaneously:
Developer A: T011 → T012 → T013 → T014 (US1: Discovery refactor)
Developer B: T015 → T016 → T017         (US2: Dependency refactor)
Developer C: T018 → T019 → T020 → T021  (US3: TFS implementation)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T005) — create types in Abstractions
2. Complete Phase 2: Foundational (T006–T010) — ADO implementation + tests
3. Complete Phase 3: User Story 1 (T011–T014) — discovery refactor
4. **STOP and VALIDATE**: Run inventory scenario, verify field projection works
5. Proceed to US2, US3 incrementally

### Incremental Delivery

1. Setup + Foundational → Types + ADO implementation ready
2. Add US1 → Inventory uses fetch service → validate independently
3. Add US2 → Dependency analysis pre-filters → validate independently
4. Add US3 → TFS implementation → validate with TFS source
5. Documentation Sync → all discrepancies resolved
6. Each story adds value without breaking previous stories
