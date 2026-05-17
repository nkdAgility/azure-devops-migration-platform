# Implementation Plan: Field Filter Scope for Work Items

**Branch**: `014-field-filter-scope` | **Date**: 2026-04-19 (revised post-merge) | **Spec**: [spec.md](./spec.md)

---

## Summary

Add a `filter` scope type to the `WorkItems` module (export, import, inventory, and discovery) that includes or excludes work items based on a regex match against a named ADO field. Add a `Scopes` array to `OrganisationEntry` and `ScopedOrganisationEndpoint` to carry `wiql` and `filter` scopes through the job contract.

**Architecture revision (post-merge of specs 015/016/017):**
- `DiscoveryJobOrganisation` no longer exists — replaced by `ScopedOrganisationEndpoint`
- `OrganisationEntry` is now an abstract base class — `Scopes` goes on the base
- `IWorkItemFetchService` is now the canonical way to load work items from a source system — the export pre-filter pass MUST use it (supersedes spec 015 FR-012)
- `WorkItemFieldFilterOptions` is a runtime predicate type (placeholder from spec 015); the config-level `{ mode, field, pattern }` scope is parsed into it by extending `FilterOperator` with a `Regex` value
- `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` gains `WorkItemFetchScope? scope = null` so inventory/discovery can flow filter options through `IWorkItemFetchService`

---

## Technical Context

**Language/Version**: C# 12 / .NET 10; Abstractions multi-targets `net481;net10.0`
**Primary Dependencies**: MSTest, Reqnroll, Moq — all present
**Storage**: `IArtefactStore` (existing) — no new storage
**Testing**: MSTest + Reqnroll
**Performance Goals**: Filtering adds zero extra API calls; export pre-filter pass fetches only filter-referenced fields; regex evaluated per item with 2-second internal timeout
**Constraints**: Import reads from the package filesystem, not a source API — `IWorkItemFetchService` does NOT apply to the import read path

---

## Constitution Check

- [x] **Package-First**: Filter evaluation is in-process; no direct source-to-target paths
- [x] **Streaming**: Export and import remain streaming; filter is a per-item gate, not a buffer
- [x] **WorkItems Layout**: No changes to folder layout; filter causes skips, not restructuring
- [x] **Checkpointing**: Skipped items do not advance the cursor (they never start)
- [x] **Module Isolation**: All new config types in `Abstractions`; evaluation stays in callers
- [x] **Separation of Planes**: Filter types and evaluation are in the Job Engine boundary only
- [x] **Determinism**: Filter is deterministic given same field values and pattern
- [x] **ATDD-First**: All 3 user stories have Given/When/Then scenarios in spec.md
- [x] **SOLID & DI**: Existing `WorkItemFieldFilterEvaluator` extended; no parallel chain hierarchy

---

## Design Decisions

### FilterOperator extension

`FilterOperator` (in `Abstractions`) gains a `Regex` value. The existing `Equals`/`NotEquals`/`Contains` operators are retained for `IWorkItemFetchService` callers (spec 015 use cases). Config-level filter scopes map to `WorkItemFieldFilterOptions(FieldName: field, Operator: Regex, Value: pattern)`. A 2-second `Regex.IsMatch` timeout is used; `RegexMatchTimeoutException` is caught at the caller layer, logged as a warning, and treated as a non-match.

### WorkItemFieldFilterEvaluator extension

`WorkItemFieldFilterEvaluator.EvaluateFilter` gains a `Regex` case:

```csharp
case FilterOperator.Regex:
    try { return Regex.IsMatch(fieldStr, filterStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
    catch (RegexMatchTimeoutException) { throw; }  // propagates to caller
```

`WorkItemFieldFilterEvaluator.PassesFilters` propagates `RegexMatchTimeoutException`; callers catch and handle it.

### include/exclude mode

The `include`/`exclude` mode is a config-level concept, not a runtime predicate property. Callers (export orchestrator, inventory service, import pre-pass) build a `HashSet<int>` of IDs that *pass* all filters. Items not in the set are excluded. For `include` mode: items where `PassesFilters == false` are skipped. For `exclude` mode: items where `PassesFilters == true` are skipped. Both modes use the same `WorkItemFieldFilterOptions(FieldName, Regex, pattern)` — the mode inversion is applied by the caller.

`WorkItemsModuleExtensions.FromModule()` exposes two new properties:
- `IReadOnlyList<WorkItemFieldFilterOptions> IncludeFilters` — parsed from `mode: include` filter scopes
- `IReadOnlyList<WorkItemFieldFilterOptions> ExcludeFilters` — parsed from `mode: exclude` filter scopes

### Export pre-filter pass via IWorkItemFetchService

`WorkItemExportOrchestrator` gains an optional constructor parameter `IWorkItemFetchService? fetchService = null` and `IReadOnlyList<WorkItemFieldFilterOptions>? includeFilters = null, IReadOnlyList<WorkItemFieldFilterOptions>? excludeFilters = null`.

Before the main revision export loop, if filters are configured:
1. Call `fetchService.FetchAsync(endpoint, project, new WorkItemFetchScope(Fields: filterFields), ct)`
2. Evaluate each `FetchedWorkItem` against include/exclude filters
3. Build `HashSet<int> _filteredWorkItemIds` of IDs that *pass*
4. Main loop skips any work item ID not in the set

Only filter-referenced field names are fetched in the pre-pass — minimal payload. Filtered-out items incur zero revision API calls.

### Inventory/discovery via IWorkItemDiscoveryService scope parameter

`IWorkItemDiscoveryService.DiscoverWorkItemsAsync` gains:
```csharp
WorkItemFetchScope? scope = null
```

Implementation (`AzureDevOpsWorkItemDiscoveryService`): merges `scope?.Fields` with `["System.Rev"]` into a union; uses `scope?.FilterOptions` and `scope?.BaseQuery` when building the inner `WorkItemFetchScope` passed to `IWorkItemFetchService.FetchAsync`. Items that fail the filter are not counted in the discovery summary.

`InventoryService.RunInventoryAsync` builds a `WorkItemFetchScope` per org from `ScopedOrganisationEndpoint.Scopes`:
- `wiql` scope → `BaseQuery`
- `filter` scopes → `FilterOptions` (include filters as `WorkItemFieldFilterOptions`)
- Passes the scope as the new parameter to `_workItemDiscovery.DiscoverWorkItemsAsync`

`exclude` mode on org-level filter scopes is handled by `InventoryService` negating the filter via the caller-layer inversion pattern.

### Import pre-pass (filesystem — no IWorkItemFetchService)

Import reads from the package filesystem. A lightweight pre-pass enumerates revision folder names only (no file reads) to identify the last revision folder per work item ID. Only those `revision.json` files are read. Field values are evaluated against include/exclude filters, building `HashSet<int> _filteredWorkItemIds`. The main streaming import pass skips all revision folders for IDs in the filtered set.

### ScopedOrganisationEndpoint.Scopes

```csharp
public IReadOnlyList<JobModuleScope> Scopes { get; init; } = Array.Empty<JobModuleScope>();
```

`InventoryCommand` maps `OrganisationEntry.Scopes` (parsed from `MigrationOptionsScope` list) → `JobModuleScope` list on `ScopedOrganisationEndpoint`. `InventoryServiceFactory` passes the scopes through when building `DiscoveryOptions` org entries (they are carried on `AzureDevOpsOrganisationEntry` which also inherits `Scopes` from the abstract base).

### OrganisationEntry.Scopes

Added to the **abstract base class** `OrganisationEntry`:
```csharp
public List<MigrationOptionsScope> Scopes { get; set; } = new();
```

`InventoryService.RunInventoryAsync` reads `entry.Scopes` when building the per-org `WorkItemFetchScope`.

### Validation

`WorkItemsModuleExtensions.FromModule()` calls validation on each parsed filter scope:
- `mode` must be `"include"` or `"exclude"` (case-insensitive)
- `field` must be non-empty
- `pattern` must be a valid .NET regex (validated via `Regex(pattern)` construction in a `try/catch`)

Fails fast with `InvalidOperationException` before the job is submitted.

### Diagnostic log levels for filter skips

- **Information**: `"Work item {id} skipped — filter field='{field}' mode='{mode}'"`
- **Debug** (`ILogger.IsEnabled` guard): appends the actual field value
- **Trace** (`ILogger.IsEnabled` guard): appends the pattern

### Zero-match warning

After export/import/inventory completes, if `workItemsProcessed == 0` and at least one filter was configured, emit: `"[WorkItems] Warning: all work items were filtered out by filter scopes. Check your filter configuration."`

---

## Project Structure

### Source Code Changes

```
src/
  DevOpsMigrationPlatform.Abstractions/
    Models/
      FilterOperator.cs              ← EXTEND: add Regex operator value
      WorkItemFieldFilterEvaluator.cs ← EXTEND: add Regex case with 2s timeout; propagate RegexMatchTimeoutException
    Options/
      OrganisationEntry.cs           ← MODIFY: add List<MigrationOptionsScope> Scopes to abstract base
    Models/
      ScopedOrganisationEndpoint.cs  ← MODIFY: add IReadOnlyList<JobModuleScope> Scopes
    Modules/
      WorkItemsModuleExtensions.cs   ← EXTEND: parse filter scopes; expose IncludeFilters / ExcludeFilters

  DevOpsMigrationPlatform.Abstractions/
    Services/
      IWorkItemDiscoveryService.cs   ← MODIFY: add WorkItemFetchScope? scope = null to DiscoverWorkItemsAsync

  DevOpsMigrationPlatform.Infrastructure/
    Export/
      WorkItemExportOrchestrator.cs  ← MODIFY: add pre-filter pass via IWorkItemFetchService
    Import/
      WorkItemImportOrchestrator.cs  ← MODIFY: add pre-pass to build filtered ID set

  DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
    Services/
      AzureDevOpsWorkItemDiscoveryService.cs ← MODIFY: accept scope param; merge fields; pass to FetchAsync

  DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
    Services/
      TfsObjectModelWorkItemDiscoveryService.cs ← MODIFY: add scope param to match interface; ignore it

  DevOpsMigrationPlatform.Infrastructure.Simulated/
    Services/
      SimulatedWorkItemDiscoveryService.cs ← MODIFY: add scope param to match interface; apply filter if non-null

  DevOpsMigrationPlatform.Infrastructure/
    Services/
      InventoryService.cs            ← MODIFY: build WorkItemFetchScope from org scopes; pass to DiscoverWorkItemsAsync

  DevOpsMigrationPlatform.CLI.Migration/
    Commands/Discovery/
      InventoryCommand.cs            ← MODIFY: map OrganisationEntry.Scopes → ScopedOrganisationEndpoint.Scopes

tests/
  DevOpsMigrationPlatform.Infrastructure.Tests/
    Models/
      WorkItemFieldFilterEvaluatorTests.cs ← EXTEND: add Regex operator tests, timeout propagation
    Export/
      WorkItemExportOrchestratorTests.cs   ← EXTEND: filter skip scenarios; no-revision-fetch assertion
    Import/
      WorkItemImportOrchestratorTests.cs   ← EXTEND: filter skip on last revision; log assertions
    Inventory/
      InventoryServiceTests.cs             ← EXTEND: wiql scope + filter scope passed as WorkItemFetchScope
      WorkItemQueryWindowStrategyTests.cs  ← no change expected
    Compatibility/
      BackwardCompatFilterTests.cs         ← NEW: deserialization; empty filter list when no scopes

features/
  export/work-items/
    filter-scope-export.feature      ← NEW: US1 acceptance scenarios
  import/work-items/
    filter-scope-import.feature      ← NEW: US3 acceptance scenarios
  inventory/work-items/
    filter-scope-inventory.feature   ← NEW: US2 filter scope acceptance scenarios

scenarios/
  regression-no-filter-scopes.json  ← NEW: backward-compat regression config
```

---

## Complexity Tracking

| Area | Complexity | Rationale |
|---|---|---|
| FilterOperator + Evaluator extension | Low | Additive; existing static class |
| OrganisationEntry.Scopes | Low | One property on abstract base |
| ScopedOrganisationEndpoint.Scopes | Low | One property; one mapping site |
| WorkItemsModuleExtensions filter parsing | Medium | New parse branch + validation |
| IWorkItemDiscoveryService scope param | Low | Optional param; two impl updates |
| AzureDevOpsWorkItemDiscoveryService | Medium | Field union + scope forwarding |
| WorkItemExportOrchestrator pre-filter | Medium | New pre-pass; inject IWorkItemFetchService |
| WorkItemImportOrchestrator pre-pass | Medium | Folder enumeration + one-file-per-WI read |
| InventoryService scope building | Low | Build scope from entry.Scopes |
| InventoryCommand scope mapping | Low | One LINQ Select extension |
| Tests | Medium | Multiple extension points |
## Current status

Reconciled against current repository implementation on 2026-05-16: partially complete with open gaps.

## Remaining incomplete work (IDs)

T010, T013, T016, T018, T023, T025, T027, T029, T030, T032, T033.

## Completed because superseded (IDs + source)

- T015 superseded by `specs/025.1-fold-to-job/spec.md` (job materialisation replaced legacy `InventoryCommand` scope mapping path).

## Contradictions and reconciliation

- Planned file paths for several tasks are stale after architecture evolution (`*.Infrastructure`/`CLI.Migration` → `*.Infrastructure.Agent` and job-worker mapping).
- Export/import skip logging is implemented but does not yet satisfy FR-011 field/mode/pattern detail expectations.
- Inventory connector parity is incomplete for this feature (`SimulatedWorkItemDiscoveryService` accepts but ignores filter scope).
- Configuration docs remain internally inconsistent on WorkItems scope shapes and organisation-level scopes.

## Verification evidence

- Build success: `dotnet build .\\DevOpsMigrationPlatform.slnx -v minimal`.
- Targeted tests success: 66 filter-scope-related tests passed in `DevOpsMigrationPlatform.Infrastructure.Agent.Tests`.
- Required task command `dotnet clean && dotnet build --no-incremental` currently fails in this environment due file lock/copy/compiler errors.
- Full `dotnet test` currently lacks a successful completion record from this reconciliation session.
