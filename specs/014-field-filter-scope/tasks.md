# Tasks: Field Filter Scope for Work Items

**Feature Branch**: `014-field-filter-scope`  
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)  
**Created**: 2026-04-17

---

## Phase 1: Setup

- [ ] T001 Create `features/export/filter-scope-export.feature` with US1 acceptance scenarios
- [ ] T002 Create `features/import/filter-scope-import.feature` with US3 acceptance scenarios
- [ ] T003 Create `features/inventory/wiql-scope-organisation.feature` with US2 wiql scope acceptance scenarios
- [ ] T003a Create `features/inventory/filter-scope-inventory.feature` with US2 filter scope acceptance scenarios (scenario 4 from US2)

---

## Phase 2: Foundational — Abstractions (blocking all user stories)

- [ ] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Filters/WorkItemFieldFilterOptions.cs` — sealed record with `Mode` (string), `Field` (string), `Pattern` (string), and a static `Validate()` that throws `InvalidOperationException` on invalid mode/empty field/invalid regex; multi-target `net481;net10.0`
- [ ] T005 Create `src/DevOpsMigrationPlatform.Abstractions/Filters/IWorkItemFieldFilter.cs` — interface with `bool Passes(IDictionary<string, object?> fields)` and `WorkItemFieldFilterOptions Options { get; }`; multi-target `net481;net10.0`
- [ ] T006 [P] Modify `src/DevOpsMigrationPlatform.Abstractions/Options/OrganisationEntry.cs` — add `public List<MigrationOptionsScope> Scopes { get; set; } = new();`
- [ ] T007 [P] Modify `src/DevOpsMigrationPlatform.Abstractions/Models/DiscoveryJobOrganisation.cs` — add `public List<JobModuleScope> Scopes { get; init; } = new();`
- [ ] T008 Modify `src/DevOpsMigrationPlatform.Abstractions/Modules/WorkItemsModuleExtensions.cs` — parse `filter` scopes from `JobModule.Scopes` into `IReadOnlyList<WorkItemFieldFilterOptions> Filters`; call `Validate()` on each; expose as new property; fall back to empty list when none present

---

## Phase 3: User Story 1 — Filter work items by area path during export (Priority: P1)

**Story Goal**: Export only work items whose field value matches a `filter` scope regex.

**Independent Test Criteria**: Run export with `include` filter on `System.AreaPath`. Package contains only matching items. Run with `exclude` filter — package contains no matching-pattern items.

### Implementation for User Story 1

- [ ] T009 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Filters/WorkItemFieldFilter.cs` — implements `IWorkItemFieldFilter`; `Passes()` converts field value to string (null → empty string), applies `Regex.IsMatch` with 2-second timeout; `include` mode returns match result; `exclude` mode returns inverse; absent field returns `false` for include and `true` for exclude; does **not** catch `RegexMatchTimeoutException` — lets it propagate to the chain
- [ ] T010 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Filters/WorkItemFieldFilterChain.cs` — constructor takes `IReadOnlyList<WorkItemFieldFilterOptions>` directly and constructs `WorkItemFieldFilter` instances inline (no factory); `Passes(fields)` returns `true` only if all filters pass; catches `RegexMatchTimeoutException`, logs a warning, returns `false` for include / `true` for exclude; `IsEmpty` property for zero-match warning path
- [ ] T012 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` — add optional constructor parameter `IReadOnlyList<WorkItemFieldFilterOptions>? filters = null`; build `WorkItemFieldFilterChain` at construction time; evaluate chain against current work item fields **before** requesting revision history; if rejected, add ID to `HashSet<int> _filteredWorkItemIds`, log diagnostic (Info: field+mode; Debug: +value; Trace: +pattern); skip all processing for IDs in filtered set; after loop, if `workItemsProcessed == 0` and chain non-empty emit zero-match warning
- [ ] T013 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` `ExportAsync` — pass `ext.Filters` to `WorkItemExportOrchestrator` constructor

### Tests for User Story 1

- [ ] T014 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Filters/WorkItemFieldFilterTests.cs` — unit tests: `Passes_IncludeMode_MatchingField_ReturnsTrue`, `Passes_IncludeMode_NonMatchingField_ReturnsFalse`, `Passes_ExcludeMode_MatchingField_ReturnsFalse`, `Passes_ExcludeMode_NonMatchingField_ReturnsTrue`, `Passes_IncludeMode_AbsentField_ReturnsFalse`, `Passes_ExcludeMode_AbsentField_ReturnsTrue`, `Passes_InvalidRegex_ThrowsAtConstruction` (validation), `Passes_NullFieldValue_TreatedAsEmpty`, `Passes_RegexTimeout_PropagatesRegexMatchTimeoutException`
- [ ] T015 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Filters/WorkItemFieldFilterChainTests.cs` — unit tests: `Passes_AllFiltersPass_ReturnsTrue`, `Passes_OneFilterFails_ReturnsFalse`, `Passes_EmptyChain_ReturnsTrue`, `IsEmpty_ReturnsTrue_WhenNoFilters`, `Passes_RegexTimeout_IncludeMode_ReturnsFalse_AndLogsWarning`, `Passes_RegexTimeout_ExcludeMode_ReturnsTrue_AndLogsWarning`
- [ ] T016 [P] [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs` — add: `ExportAsync_WithIncludeFilter_SkipsNonMatchingWorkItems`, `ExportAsync_WithExcludeFilter_SkipsMatchingWorkItems`, `ExportAsync_WhenZeroItemsPassFilter_EmitsWarning`, `ExportAsync_WithFilter_DoesNotFetchRevisions_ForFilteredItems`, `ExportAsync_FilterSkip_LogsFieldAndMode_AtInformationLevel`
- [ ] T017 [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/FilterScopeExportSteps.cs` + `FilterScopeExportContext.cs` — Reqnroll step definitions wiring `features/export/filter-scope-export.feature` scenarios

---

## Phase 4: User Story 2 — Custom WIQL scope on organisation entry (Priority: P2)

**Story Goal**: An organisation entry's `wiql` scope replaces the platform default query in inventory.

**Independent Test Criteria**: Inventory run with custom `wiql` scope on one org returns count matching that query, not the full project count.

### Implementation for User Story 2

- [ ] T018 [US2] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — when mapping `OrganisationEntry` → `DiscoveryJobOrganisation`, copy `entry.Scopes.Select(s => new JobModuleScope { Type = s.Type, Parameters = ... }).ToList()` to `DiscoveryJobOrganisation.Scopes`
- [ ] T019 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Factories/InventoryServiceFactory.cs` `BuildDiscoveryOptions()` — for each org, extract first `wiql` scope query from `DiscoveryJobOrganisation.Scopes`; if present, carry it through to `OrganisationEntry.Scopes` on the resulting `DiscoveryOptions`
- [ ] T020 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure/Services/InventoryService.cs` `RunInventoryAsync` — extract `wiql` scope query from org scopes and pass as `baseQuery`; build `WorkItemFieldFilterOptions` list from `filter` scopes on the org; pass as `filterOptions` to `_workItemDiscovery.DiscoverWorkItemsAsync`
- [ ] T020a [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemDiscoveryService.cs` `DiscoverWorkItemsAsync` — accept new `filterOptions` parameter; when non-empty, union filter field reference names into the `fields:` array of the existing `GetWorkItemsAsync` batch call; build `WorkItemFieldFilterChain` from options; evaluate chain against each returned work item's fields; skip non-matching items (do not count them in summary); log diagnostic at Info/Debug/Trace levels for filtered items
- [ ] T020b [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Services/TfsObjectModelWorkItemDiscoveryService.cs` `DiscoverWorkItemsAsync` — add `filterOptions` parameter to match updated interface signature; ignore it (TFS filter evaluation is out of scope)
- [ ] T020c [US2] Modify `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemDiscoveryService.cs` — add `IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions = null` parameter to `DiscoverWorkItemsAsync` (breaking interface change)
- [ ] T021 [US2] Verify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` — confirm `options.BaseQuery` already flows through correctly; if not, add the plumbing

### Tests for User Story 2

- [ ] T022 [P] [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WiqlScopeOrganisationTests.cs` — unit tests: `InventoryService_WithWiqlScope_UsesCustomQuery`, `InventoryService_WithNoWiqlScope_UsesPlatformDefault`, `InventoryService_WithEmptyWiqlScopeQuery_UsesPlatformDefault`
- [ ] T022a [P] [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/FilterScopeInventoryTests.cs` — unit tests: `InventoryService_WithIncludeFilterScope_CountsOnlyMatchingItems`, `InventoryService_WithExcludeFilterScope_ExcludesMatchingItems`, `InventoryService_WithOrgFilterScope_LogsFilteredItemAtInformationLevel`
- [ ] T023 [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WiqlScopeOrganisationSteps.cs` + `WiqlScopeOrganisationContext.cs` — Reqnroll step definitions for `features/inventory/wiql-scope-organisation.feature`

---

## Phase 5: User Story 3 — Filter scope applies consistently to import (Priority: P3)

**Story Goal**: Import-side filter evaluates last revision's fields and skips non-matching work items.

**Independent Test Criteria**: Import run with `exclude` filter on area path; work items in excluded path not created in target.

### Implementation for User Story 3

- [ ] T024 [US3] Modify `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — add optional constructor parameter `WorkItemFieldFilterChain? filterChain = null`; perform a lightweight pre-pass enumerating revision folder names only (no file reads) to identify the last revision folder per work item ID; read `revision.json` from each last-revision folder to build a `HashSet<int> _filteredWorkItemIds` of work items that fail the filter chain; log diagnostic (Info: field+mode; Debug: +value; Trace: +pattern) for each filtered item; main streaming pass skips all folders for IDs in the filtered set; after loop, emit zero-match warning if `workItemsProcessed == 0` and chain non-empty
- [ ] T025 [US3] Modify `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` `ImportAsync` — construct `WorkItemFieldFilterChain` from `ext.Filters` and pass to `WorkItemImportOrchestrator`

### Tests for User Story 3

- [ ] T026 [P] [US3] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/WorkItemImportOrchestratorTests.cs` — add: `ImportAsync_WithIncludeFilter_SkipsNonMatchingWorkItems`, `ImportAsync_WithExcludeFilter_SkipsMatchingWorkItems`, `ImportAsync_FilterEvaluatesLastRevision_NotFirstRevision`, `ImportAsync_WhenZeroItemsPassFilter_EmitsWarning`, `ImportAsync_FilterSkip_LogsFieldAndMode_AtInformationLevel`
- [ ] T027 [US3] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/FilterScopeImportSteps.cs` + `FilterScopeImportContext.cs` — Reqnroll step definitions for `features/import/filter-scope-import.feature`

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T028 Update `docs/configuration.md` — add `filter` scope type rows to WorkItems Module table; add `scopes` property to organisation entry in Full Schema example and Top-Level Fields table; note `filter` scope on orgs IS evaluated in inventory and discovery
- [ ] T029 Update `docs/modules.md` — extend `WorkItemsModule` responsibility description to mention `filter` scope types
- [ ] T030 Resolve all items in `specs/014-field-filter-scope/discrepancies.md` — mark each as `Resolved` after doc updates
- [ ] T031 [P] Update scenario config files — add `filter` scope example to `scenarios/queue-export-ado-workitems-inline-comments.json` as a commented-out example block
- [ ] T034 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Compatibility/BackwardCompatFilterTests.cs` — unit tests: `OrganisationEntry_WithNoScopesProperty_DeserializesWithEmptyList`, `WorkItemsModuleExtensions_WhenNoFilterScopes_ReturnsEmptyFilterList`, `WorkItemFieldFilterChain_WithEmptyOptions_IsEmpty_ReturnsTrue`
- [ ] T035 [P] Create `scenarios/regression-no-filter-scopes.json` — mirrors `scenarios/queue-export-ado-workitems-single-project.json` exactly; confirms existing configs with no `scopes` property continue to function (backward compatibility regression config)
- [ ] T036 [P] Add performance guidance to `docs/configuration.md` filter scope section — note that each filter field name is added to the inventory batch call payload (200 items per batch); advise users to minimise filter scope count and prefer short reference-data fields (`System.AreaPath`, `System.WorkItemType`, `System.State`) over large text fields (`System.Description`, `System.History`)
- [ ] T037 [P] Add unit test `AzureDevOpsWorkItemDiscoveryService_WithFilterScopes_IncludesFilterFieldsInBatchRequest` to `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/` (or nearest ADO test project) — asserts the `fields:` array passed to `GetWorkItemsAsync` contains `System.Rev` unioned with each filter scope's `Field` property; verifies SC-006
- [ ] T032 Run `dotnet clean && dotnet build --no-incremental` — MUST pass before declaring done
- [ ] T033 Run `dotnet test` — ALL tests MUST pass before declaring done

---

## Dependencies

```
T004, T005 → T008 → T012 → T013 → T016, T017
T004, T005 → T020c → T020b, T020a, T020
T006       → T018 → T019 → T020 → T022, T022a, T023
T007       → T018
T009, T010 → T012
T012       → T013
T020c      → T020a, T020b
T020a      → T022a, T037
T020       → T022a
T024       → T025
T028, T029, T036 → T030
T034, T035 → T032
T036, T037 → T032
T032       → T033
```

## Parallel Execution Opportunities

| Group | Tasks | Why parallelisable |
|---|---|---|
| Abstractions | T006, T007 | Different files, no dependencies on each other |
| Interface implementations | T020a, T020b | Different projects, same interface change |
| Filter unit tests | T014, T015, T016 | Independent test files |
| Inventory tests | T022, T022a | Different test files, same phase |
| Story 2 + Story 3 setup | T018–T021 vs T024–T025 | Different orchestrators |
| Doc updates | T028, T029, T031, T036 | Different files |
| Backward compat | T034, T035 | Independent of each other |

## Implementation Strategy

**MVP (US1 only — T001 through T017)**: Export-side filter on `WorkItems` module. Delivers the core value. US2 and US3 are additive and can follow independently.

**Suggested sequence**: T001→T003a (features), T004→T008+T020c (abstractions + interface), T009→T013 (filter impl + export wiring), T014→T017 (tests), then T018→T023+T022a+T020a+T020b+T037 (inventory wiql+filter), then T024→T027 (import filter), then T028→T036 (docs + backward compat + regression), then T032→T033 (build + test).
