# Tasks: Field Filter Scope for Work Items

**Feature Branch**: `014-field-filter-scope`
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Revised**: 2026-04-19 — updated to reflect post-merge architecture (specs 015/016/017 canonical)

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 = export filter, US2 = org scopes / inventory filter, US3 = import filter

---

## Phase 1: Setup — Feature Files (no code dependencies)

- [X] T001 [P] Create `features/export/work-items/filter-scope-export.feature` — US1 acceptance scenarios (include filter, exclude filter, AND logic, absent field) - Status: complete
- [X] T002 [P] Create `features/import/work-items/filter-scope-import.feature` — US3 acceptance scenarios (last-revision evaluation, skip+log, zero-match warning) - Status: complete
- [X] T003 [P] Create `features/inventory/work-items/filter-scope-inventory.feature` — US2 filter scope acceptance scenarios (org-level filter counted, wiql scope + filter combined) - Status: complete

---

## Phase 2: Foundational — Abstractions (blocking all user stories)

- [X] T004 Extend `src/DevOpsMigrationPlatform.Abstractions/Models/FilterOperator.cs` — add `Regex` enum value with XML doc-comment; keep existing `Equals`/`NotEquals`/`Contains` - Status: complete
- [X] T005 Extend `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemFieldFilterEvaluator.cs` — add `Regex` case in `EvaluateFilter`: `Regex.IsMatch(fieldStr, filterStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2))`; propagate `RegexMatchTimeoutException` (do not catch here); remove "Placeholder for feature 014" comment - Status: complete
- [X] T006 [P] Modify `src/DevOpsMigrationPlatform.Abstractions/Options/OrganisationEntry.cs` — add `public List<MigrationOptionsScope> Scopes { get; set; } = new();` to the abstract base class - Status: complete
- [X] T007 [P] Modify `src/DevOpsMigrationPlatform.Abstractions/Models/ScopedOrganisationEndpoint.cs` — add `public IReadOnlyList<JobModuleScope> Scopes { get; init; } = Array.Empty<JobModuleScope>();` - Status: complete
- [X] T008 Modify `src/DevOpsMigrationPlatform.Abstractions/Modules/WorkItemsModuleExtensions.cs` — parse `filter` scopes from `JobModule.Scopes`; validate each (`mode`, `field`, `pattern`); expose `IReadOnlyList<WorkItemFieldFilterOptions> IncludeFilters` and `IReadOnlyList<WorkItemFieldFilterOptions> ExcludeFilters`; extend `FromModule` return type or add new properties; add `Validate()` logic (fail fast on invalid mode/empty field/invalid regex) - Status: complete
- [X] T009 Modify `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemDiscoveryService.cs` — add `WorkItemFetchScope? scope = null` optional parameter to `DiscoverWorkItemsAsync` signature (breaking change — platform-controlled) - Status: complete

---

## Phase 3: User Story 1 — Export filter via IWorkItemFetchService pre-pass (Priority: P1)

**Goal**: Export only work items whose field values pass the filter scopes; no revision API calls for filtered items.

### Implementation for User Story 1

- [ ] T010 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`: - Status: incomplete
  - Add optional constructor params: `IWorkItemFetchService? fetchService = null`, `IReadOnlyList<WorkItemFieldFilterOptions>? includeFilters = null`, `IReadOnlyList<WorkItemFieldFilterOptions>? excludeFilters = null`, `OrganisationEndpoint? filterEndpoint = null`, `string? filterProject = null`
  - Before the main revision loop (when any filters configured): call `fetchService.FetchAsync(filterEndpoint, filterProject, new WorkItemFetchScope(Fields: filterFields), ct)` to build `HashSet<int> _passedFilterIds`
  - `include` mode: skip items where `PassesFilters == false`; `exclude` mode: skip items where `PassesFilters == true`
  - Catch `RegexMatchTimeoutException` per item, log warning, treat as non-match (include = skip, exclude = pass)
  - Log skipped items at Info/Debug/Trace levels per spec FR-011
  - Emit zero-match warning when `workItemsProcessed == 0` and filters were configured
  - Evidence: `WorkItemExportOrchestrator` pre-filter and zero-match warning exist, but skip logging does not include field/mode/pattern detail (`src\DevOpsMigrationPlatform.Infrastructure.Agent\Export\WorkItemExportOrchestrator.cs`).
- [X] T011 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` `ExportAsync` — pass `ext.IncludeFilters`, `ext.ExcludeFilters`, `fetchService`, `endpoint`, `project` to `WorkItemExportOrchestrator` constructor - Status: complete

### Tests for User Story 1

- [X] T012 [P] [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Models/WorkItemFieldFilterEvaluatorTests.cs` — add: `Regex_MatchingPattern_ReturnsTrue`, `Regex_NonMatchingPattern_ReturnsFalse`, `Regex_AbsentField_ReturnsFalse`, `Regex_NullValue_TreatedAsEmpty`, `Regex_Timeout_PropagatesRegexMatchTimeoutException` - Status: complete
- [ ] T013 [P] [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs` — add: `ExportAsync_WithIncludeFilter_SkipsNonMatchingItems`, `ExportAsync_WithExcludeFilter_SkipsMatchingItems`, `ExportAsync_WhenZeroItemsPassFilter_EmitsWarning`, `ExportAsync_WithFilter_DoesNotFetchRevisions_ForFilteredItems`, `ExportAsync_FilterSkip_LogsFieldAndMode` - Status: incomplete
  - Evidence: filter tests exist, but no `FilterSkip_LogsFieldAndMode` equivalent assertion exists in `WorkItemExportOrchestratorTests.cs`.
- [X] T014 [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/FilterScopeExportSteps.cs` + `FilterScopeExportContext.cs` — Reqnroll step definitions for `features/export/work-items/filter-scope-export.feature` - Status: complete

---

## Phase 4: User Story 2 — Org-level scopes in inventory (Priority: P2)

**Goal**: Org-level `wiql` and `filter` scopes flow from config → `ScopedOrganisationEndpoint` → `InventoryService` → `IWorkItemDiscoveryService` → `IWorkItemFetchService`.

### Implementation for User Story 2

- [X] T015 [US2] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — when building `ScopedOrganisationEndpoint`, map `entry.Scopes.Select(s => new JobModuleScope { Type = s.Type, Parameters = s.Parameters }).ToList()` to `Scopes` property - Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md
  - Evidence: `InventoryCommand` path no longer exists; scope mapping is now performed in job materialisation (`src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs:861-870`).
- [ ] T016 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemDiscoveryService.cs` — accept `WorkItemFetchScope? scope` in `DiscoverWorkItemsAsync`; union `scope?.Fields` with `["System.Rev"]` into merged fields list; pass `new WorkItemFetchScope(Fields: merged, FilterOptions: scope?.FilterOptions, BaseQuery: scope?.BaseQuery)` to `_fetchService.FetchAsync`; catch `RegexMatchTimeoutException` per item — log warning, skip item; items that fail the filter are not counted in summary - Status: incomplete
  - Evidence: scope union/forwarding is implemented, but no `RegexMatchTimeoutException` handling exists in discovery path (`src\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\Discovery\AzureDevOpsWorkItemDiscoveryService.cs`).
- [X] T017 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Services/TfsObjectModelWorkItemDiscoveryService.cs` — add `WorkItemFetchScope? scope = null` to `DiscoverWorkItemsAsync` to match interface; ignore the parameter - Status: complete
- [ ] T018 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemDiscoveryService.cs` — add `WorkItemFetchScope? scope = null` to `DiscoverWorkItemsAsync`; apply `WorkItemFieldFilterEvaluator.PassesFilters` if `scope?.FilterOptions` is non-empty - Status: incomplete
  - Evidence: simulated discovery accepts `scope` but explicitly ignores filter options (`src\DevOpsMigrationPlatform.Infrastructure.Simulated\Discovery\SimulatedWorkItemDiscoveryService.cs:48-50`).
- [X] T019 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure/Services/InventoryService.cs` `RunInventoryAsync` — for each org entry, read `entry.Scopes` (on `OrganisationEntry`); extract first `wiql` scope query as `BaseQuery`; extract `filter` scopes and map to `WorkItemFieldFilterOptions` list (include mode) and an exclusion set; build `WorkItemFetchScope`; pass as `scope` to `_workItemDiscovery.DiscoverWorkItemsAsync`; apply exclude-mode inversion at this layer - Status: complete

### Tests for User Story 2

- [X] T020 [P] [US2] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/InventoryServiceTests.cs` — add: `RunInventoryAsync_WithWiqlScope_PassesBaseQueryToDiscovery`, `RunInventoryAsync_WithNoWiqlScope_UsesNullBaseQuery`, `RunInventoryAsync_WithIncludeFilterScope_PassesFilterOptionsToDiscovery`, `RunInventoryAsync_WithExcludeFilterScope_InvertsFilter` - Status: complete
- [X] T021 [P] [US2] Add unit test `AzureDevOpsWorkItemDiscoveryService_WithFilterScope_UnionsFieldsWithSystemRev` to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/` — asserts the `WorkItemFetchScope.Fields` passed to `IWorkItemFetchService.FetchAsync` contains `System.Rev` unioned with each filter field; verifies SC-006 - Status: complete
- [X] T022 [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/FilterScopeInventorySteps.cs` + `FilterScopeInventoryContext.cs` — Reqnroll step definitions for `features/inventory/work-items/filter-scope-inventory.feature` - Status: complete

---

## Phase 5: User Story 3 — Import filter via filesystem pre-pass (Priority: P3)

**Goal**: Import skips work items whose last-revision fields fail the filter; uses filesystem pre-pass, not `IWorkItemFetchService`.

### Implementation for User Story 3

- [ ] T023 [US3] Modify `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs`: - Status: incomplete
  - Add optional constructor params: `IReadOnlyList<WorkItemFieldFilterOptions>? includeFilters = null`, `IReadOnlyList<WorkItemFieldFilterOptions>? excludeFilters = null`
  - Pre-pass: enumerate revision folder names only (no file reads) to find the last revision folder per work item ID; read one `revision.json` per distinct work item; evaluate against include/exclude filters; build `HashSet<int> _filteredWorkItemIds`
  - Catch `RegexMatchTimeoutException` per item — log warning, treat as non-match
  - Main streaming pass skips all revision folders for IDs in the filtered set
  - Log skipped items at Info/Debug/Trace levels per FR-011
  - Emit zero-match warning if `workItemsProcessed == 0` and filters configured
  - Evidence: pre-pass/skip and zero-match warning are implemented, but skip logging does not include field/mode/pattern detail (`src\DevOpsMigrationPlatform.Infrastructure.Agent\Import\WorkItemImportOrchestrator.cs`).
- [X] T024 [US3] Modify `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` `ImportAsync` — pass `ext.IncludeFilters`, `ext.ExcludeFilters` to `WorkItemImportOrchestrator` constructor - Status: complete

### Tests for User Story 3

- [ ] T025 [P] [US3] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/WorkItemImportOrchestratorTests.cs` — add: `ImportAsync_WithIncludeFilter_SkipsNonMatchingItems`, `ImportAsync_WithExcludeFilter_SkipsMatchingItems`, `ImportAsync_FilterEvaluatesLastRevision_NotFirstRevision`, `ImportAsync_WhenZeroItemsPassFilter_EmitsWarning`, `ImportAsync_FilterSkip_LogsFieldAndMode` - Status: incomplete
  - Evidence: `WorkItemImportOrchestratorFilterTests` covers filtering behavior, but no `FilterSkip_LogsFieldAndMode` equivalent assertion exists.
- [X] T026 [US3] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/FilterScopeImportSteps.cs` + `FilterScopeImportContext.cs` — Reqnroll step definitions for `features/import/work-items/filter-scope-import.feature` - Status: complete

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T027 [P] Update `docs/configuration-reference.md` — add `filter` scope type rows to WorkItems Module table; add `scopes` to org entry in Full Schema example; add performance guidance (prefer short fields, minimise filter count) - Status: incomplete
  - Evidence: filter rows and guidance are present, but docs still state WorkItems scope is only `wiql` and org full-schema sample still omits `scopes` (`docs\configuration-reference.md:225-226`, `196-216`).
- [X] T028 [P] Update `docs/module-development-guide.md` — extend `WorkItemsModule` responsibility description to mention `filter` scope type - Status: complete
- [ ] T029 Resolve all items in `specs/014-field-filter-scope/discrepancies.md` — mark each as `Resolved` after doc updates - Status: incomplete
  - Evidence: discrepancies are marked resolved, but underlying items remain open (e.g., placeholder comment in `WorkItemFieldFilterOptions`, configuration doc mismatch).
- [ ] T030 [P] ~~Create `tests/.../Compatibility/BackwardCompatFilterTests.cs`~~ — deferred; not needed until go-live - Status: incomplete
  - Evidence: no `BackwardCompatFilterTests.cs` file exists under `tests\`.
- [X] T031 [P] Create `scenarios/regression-no-filter-scopes.json` — mirrors `scenarios/queue-export-ado-workitems-single-project.json` exactly; confirms existing configs with no `scopes` property continue to function - Status: complete
- [ ] T032 Run `dotnet clean && dotnet build --no-incremental` — MUST pass before declaring done - Status: incomplete
  - Evidence: `dotnet clean && dotnet build --no-incremental -v minimal` failed in this reconciliation run with file-copy/compiler lock errors (`CS2012`, `MSB3021`, `MSB3027`).
- [ ] T033 Run `dotnet test` — ALL tests MUST pass before declaring done - Status: incomplete
  - Evidence: full `dotnet test` did not complete in this reconciliation session; only targeted filter-scope tests were run successfully (`66/66` passed).

---

## Dependencies

```
T004 → T005 → T012
T004, T005 → T008 → T010, T011, T023, T024
T006 → T015 → T019 → T020
T007 → T015
T008 → T010, T013, T014, T023, T025, T026
T009 → T016, T017, T018
T010 → T013, T014
T016 → T021
T019 → T020, T021, T022
T023 → T025, T026
T027, T028 → T029
T030, T031 → T032
T032 → T033
```

## Parallel Execution Opportunities

| Group | Tasks | Why parallelisable |
|---|---|---|
| Abstractions | T006, T007 | Different files |
| Feature files | T001, T002, T003 | No code dependencies |
| Evaluator + chain tests | T012, T013 | Independent test files |
| Inventory tests | T020, T021 | Different test files |
| Doc updates | T027, T028, T030, T031 | Different files |
| Story 2 impls | T017, T018 | Different projects |

## Implementation Strategy

**MVP (US1 only — T001, T004–T005, T008–T014)**: Export-side filter using `IWorkItemFetchService` pre-pass. Delivers the core value.

**Suggested sequence**: T001–T003 (feature files) → T004–T009 (abstractions) → T010–T014 (export filter) → T015–T022 (inventory/org scopes) → T023–T026 (import filter) → T027–T031 (docs + compat) → T032–T033 (build + test)
