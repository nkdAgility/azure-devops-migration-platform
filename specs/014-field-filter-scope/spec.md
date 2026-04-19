# Feature Specification: Field Filter Scope for Work Items

**Feature Branch**: `014-field-filter-scope`  
**Created**: 2026-04-17  
**Status**: Draft  
**Input**: User description: "Add Scopes to Organisations and filter scope for work item field filtering"

## Architecture References

| Document | Status |
|---|---|
| `docs/configuration.md` | ✅ Confirmed accurate — `organisations[].scopes` does not yet exist; discrepancy logged |
| `docs/modules.md` | ✅ Confirmed accurate — `wiql` only scope documented; `filter` scope not yet documented; discrepancy logged |
| `.agents/guardrails/system-architecture.md` | ✅ Confirmed — no conflicts; new scopes flow through the job contract |

## Clarifications

### Session 2026-04-17

- Q: During import, which revision's fields does a `filter` scope evaluate to decide whether to include a work item? → A: Latest revision only — the last revision stored in the package for that work item
- Q: During export, which fields are available when a `filter` scope evaluates a work item? → A: All fields returned by the work item detail API (full work item object — already loaded during export)
- Q: What happens when a valid filter scope results in zero work items being exported or imported? → A: Warning logged but run completes successfully
- Q: Does adding `scopes` to `OrganisationEntry` require a `configVersion` bump? → A: No — adding an optional array is purely additive; existing configs load unchanged with no upgrader needed
- Q: Should the regex evaluation timeout be configurable, or a fixed internal constant? → A: Fixed internal constant (2 seconds) — not exposed in configuration

### Session 2026-04-17 (Analysis Review)

- Q: During export, should the filter be evaluated before or after fetching the work item's full revision history? → A: Before — filter is evaluated on the current work item state already present in the source stream; if it fails the filter, no revision history is fetched. This avoids wasted API calls on filtered-out items.
- Q: How should `RegexMatchTimeoutException` be surfaced when the 2-second timeout fires? → A: `WorkItemFieldFilter.Passes()` re-throws `RegexMatchTimeoutException`; `WorkItemFieldFilterChain` catches it, logs a warning, and treats the evaluation as a non-match (include mode = fail, exclude mode = pass). Migration continues uninterrupted.
- Q: Should `WorkItemFieldFilterFactory` be a separate class or folded into `WorkItemFieldFilterChain`? → A: No factory class. `WorkItemFieldFilterChain` accepts `IReadOnlyList<WorkItemFieldFilterOptions>` directly and constructs evaluators inline.
- Q: Should `filter` scopes on `OrganisationEntry` be deferred to a later phase or evaluated in inventory and discovery now? → A: Filter scopes apply to ALL scoped operations — Export, Import, Inventory, and Discovery. FR-009 is revised accordingly.
- Q: Do Inventory and Discovery already load the full work item (making filter evaluation free)? → A: No. Verified in code: `AzureDevOpsWorkItemDiscoveryService.DiscoverWorkItemsAsync` only fetches `System.Rev` via `IWorkItemFetchService`. Filter-referenced field names must be unioned with `System.Rev` into the `WorkItemFetchScope.Fields` array — no extra API calls, just a slightly larger batch response.
- Q: What level of detail should the diagnostic log entry contain when a work item is skipped by a filter? → A: Structured log levels — **Information**: field name and mode; **Debug**: adds the actual field value; **Trace**: adds the pattern. `ILogger.IsEnabled()` guards MUST be used to avoid string allocation overhead at higher log levels.
- Q: What level of backward-compatibility test coverage is needed for SC-005? → A: All three levels — unit tests for deserialization and filter list parsing, existing scenario config runs (T032/T033), and a dedicated regression scenario config if the unit tests are insufficient.

### Session 2026-04-19 (Post-merge architecture reconciliation)

- Q: `WorkItemFieldFilterOptions` in the codebase is a runtime predicate type (`{FieldName, Operator, Value}`) scoped inside `WorkItemFetchScope`. The `filter` scope in config (`{mode, field, pattern}`) is a higher-level concept. Are these two levels independent, or should one replace the other? → A: They are independent levels. The config-level `filter` scope (with `mode`/`field`/`pattern`) is parsed into runtime filter predicates by the module extension parser. `WorkItemFieldFilterOptions` is the runtime predicate carrier; the config scope is its source.
- Q: Should `IWorkItemFetchService` be used everywhere work items are loaded from a source system, including the export pre-filter pass? → A: Yes — wherever work items are loaded from a source system, `IWorkItemFetchService` MUST be used. For export this means a pre-filter pass via `IWorkItemFetchService` (fetching only the filter-referenced fields) determines whether to proceed to revision history fetching. Spec 015 FR-012 ("export path must not be changed") is superseded by this principle. Import reads from the local package filesystem, not a source API — `IWorkItemFetchService` does not apply to the import read path.
- Q: Where should org-level scopes (`wiql`, `filter`) be carried in the job contract from CLI to `InventoryService`? → A: On `ScopedOrganisationEndpoint` — add `IReadOnlyList<JobModuleScope> Scopes { get; init; }` to `ScopedOrganisationEndpoint`. Scopes travel with the org entry in `DiscoveryJob.Organisations`; `InventoryService` reads them when building the `WorkItemFetchScope` for each project.
- Q: How should filter options from `ScopedOrganisationEndpoint.Scopes` reach `IWorkItemFetchService.FetchAsync` inside `AzureDevOpsWorkItemDiscoveryService`? → A: Add `WorkItemFetchScope? scope = null` to `IWorkItemDiscoveryService.DiscoverWorkItemsAsync`. The implementation merges `scope.Fields` with `["System.Rev"]` and forwards `scope.FilterOptions` and `scope.BaseQuery` to the inner `IWorkItemFetchService.FetchAsync` call. This is the cleanest architectural fit — `WorkItemFetchScope` already encapsulates all scope concerns and the implementation already builds one internally.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Filter work items by area path during export (Priority: P1)

A migration engineer is exporting work items from a large Azure DevOps project. Only work items under a specific area path subtree are in scope for migration. They add a `filter` scope to the `WorkItems` module to include only items whose `System.AreaPath` matches a regex pattern. Items outside that area path are skipped during export without error.

**Why this priority**: This is the core use case. Without per-item filtering on the module level, users must either export everything and discard unwanted items, or craft increasingly complex WIQL queries. This delivers immediate value for the most common migration scoping scenario.

**Independent Test**: Configure a `WorkItems` module with a `filter` scope set to `include` on `System.AreaPath` with a pattern matching only one area. Run an export. Verify the package contains only work items under the matching area path.

**Acceptance Scenarios**:

1. **Given** a WorkItems module with a `filter` scope `{ "mode": "include", "field": "System.AreaPath", "pattern": "^MyOrg\\\\TeamA" }`, **When** the export runs, **Then** only work items whose `System.AreaPath` value matches the pattern are written to the package
2. **Given** a WorkItems module with a `filter` scope `{ "mode": "exclude", "field": "System.AreaPath", "pattern": "^MyOrg\\\\Archived" }`, **When** the export runs, **Then** work items whose `System.AreaPath` matches the exclude pattern are skipped and not written to the package
3. **Given** a WorkItems module with multiple `filter` scopes, **When** the export runs, **Then** all filters are applied as AND conditions — a work item must pass every filter to be included
4. **Given** a `filter` scope references a field that is absent on a work item, **When** the filter is evaluated, **Then** an `include` filter rejects the item and an `exclude` filter passes it

---

### User Story 2 — Add organisation-level scopes to inventory and discovery (Priority: P2)

A platform engineer is running multi-organisation inventory. For one organisation they need to use a custom WIQL query to scope discovery to only active work items rather than the default. They also want to restrict which work items are counted to a specific area path. They add a `wiql` scope and a `filter` scope to the organisation entry. Other organisations without scopes use the platform defaults.

**Why this priority**: Organisation-level scopes (`wiql` and `filter`) enable targeted scoping of inventory and discovery without code changes. The `wiql` scope adds zero overhead. The `filter` scope adds filter-referenced field names to the existing `GetWorkItemsAsync` batch call — no extra API calls, only a slightly wider batch response.

**Independent Test**: Add a `wiql` scope restricting to `[System.State] = 'Active'` and a `filter` scope on `System.AreaPath` to one organisation. Run inventory. Verify the discovered count reflects both constraints applied together.

**Acceptance Scenarios**:

1. **Given** an organisation entry with a `wiql` scope containing a custom query, **When** inventory runs for that organisation, **Then** the custom query is used instead of the platform default
2. **Given** an organisation entry with no `wiql` scope, **When** inventory runs, **Then** the platform default query `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]` is used
3. **Given** a `wiql` scope with an empty or missing `query` parameter, **When** inventory runs, **Then** the platform default query is used (graceful fallback)
4. **Given** an organisation entry with a `filter` scope `{ "mode": "include", "field": "System.AreaPath", "pattern": "^MyOrg\\\\TeamA" }`, **When** inventory runs, **Then** only work items whose `System.AreaPath` matches the pattern are counted in inventory results

---

### User Story 3 — Filter scope applies consistently to import (Priority: P3)

A migration engineer is importing a previously exported package. The package was exported without filters but they now want to import only a subset — work items under a specific area path. They add a `filter` scope to the `WorkItems` module in their import configuration. Only work items whose **latest revision** fields match the filter are imported to the target.

**Why this priority**: Import-side filtering completes the symmetric behaviour of the feature. It is lower priority because export-side filtering already provides most of the practical value.

**Independent Test**: Run an import with a `filter` scope that excludes one area path. Verify that work items in the excluded area path are not created in the target system.

**Acceptance Scenarios**:

1. **Given** a WorkItems module configured for import with an `include` filter scope, **When** the import runs, **Then** only work items whose **last revision's** field values pass the filter are imported (earlier revisions are not evaluated)
2. **Given** a work item in the package whose last revision's field value does not match an `include` filter, **When** the import processes that item, **Then** the item is skipped and a diagnostic log entry is written recording which filter caused the skip
3. **Given** a valid filter scope that matches zero work items in the package, **When** the import completes, **Then** the run completes successfully and a warning is logged stating that zero items passed the filter

---

### Edge Cases

- What happens when the `pattern` parameter is not a valid regex? → Startup validation fails fast with a descriptive error before the run begins.
- What happens when `field` is `null` or empty? → Startup validation rejects the configuration.
- What happens when `mode` is not `"include"` or `"exclude"`? → Startup validation rejects the configuration.
- What happens when a work item has a null value for the target field? → Treated as an empty string for regex matching; `include` filter rejects, `exclude` filter passes.
- What happens when multiple `wiql` scopes are declared on the same organisation? → The first `wiql` scope is used; additional ones are ignored (consistent with WorkItems module behaviour).
- What happens when both a `wiql` scope and a `filter` scope are on an organisation in inventory? → `wiql` reduces the candidate set first (applied at WIQL query time); `filter` scope is then applied to each work item after the full work item is loaded, reducing the set further.
- What happens when a valid filter results in zero work items during export or import? → Run completes successfully; a warning is logged stating that zero items passed the filter (not an error).
- During import, which revision is evaluated by a `filter` scope? → The last revision stored in the package for that work item. Earlier revisions are not evaluated.
- During export, are all work item fields available for filter evaluation? → Yes — all fields from the full work item detail API response are available; the filter is not restricted to fields listed in the WIQL `SELECT` clause.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `WorkItems` module's `scopes` array MUST accept a scope of type `"filter"` with parameters `mode` (`"include"` or `"exclude"`), `field` (ADO field reference name), and `pattern` (.NET regex string)
- **FR-002**: The export orchestrator MUST perform a pre-filter pass using `IWorkItemFetchService.FetchAsync` (fetching only filter-referenced fields) before requesting revision history. Work items that fail the filter are not fetched further — no revision history API calls are made for them. Spec 015 FR-012 is superseded by this principle: `IWorkItemFetchService` MUST be used wherever work items are loaded from a source system.
- **FR-003**: Multiple `filter` scopes MUST be combined with AND logic — a work item must pass all filters to be included in the operation
- **FR-004**: An `include` filter on a field absent from the work item MUST reject the item; an `exclude` filter on an absent field MUST pass the item
- **FR-005**: An invalid regex in the `pattern` parameter MUST cause startup validation to fail with a descriptive error before the run begins
- **FR-005a**: The regex engine MUST apply a fixed internal evaluation timeout of 2 seconds per filter evaluation to guard against catastrophic backtracking; this timeout is NOT configurable
- **FR-006**: `OrganisationEntry` (the abstract config-layer base class) MUST accept a `scopes` array of `MigrationOptionsScope` entries; concrete subclasses inherit this property
- **FR-006a**: `ScopedOrganisationEndpoint` (the job-contract type used in `DiscoveryJob.Organisations`) MUST accept a `IReadOnlyList<JobModuleScope> Scopes` property so that org-level scopes travel from CLI to `InventoryService`
- **FR-007**: An organisation `wiql` scope MUST supply its `query` parameter as `WorkItemFetchScope.BaseQuery` when `InventoryService` builds the scope for `IWorkItemDiscoveryService.DiscoverWorkItemsAsync`
- **FR-008**: An organisation with no `wiql` scope MUST fall back to the platform default WIQL query `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]`
- **FR-009**: The `filter` scope on organisations MUST be evaluated during inventory and discovery via `IWorkItemFetchService.FetchAsync`. `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` MUST accept an optional `WorkItemFetchScope? scope = null` parameter; the implementation merges the caller's `scope.Fields` with `["System.Rev"]` and forwards `scope.FilterOptions` and `scope.BaseQuery` to `IWorkItemFetchService.FetchAsync`. No additional API calls are required.
- **FR-010**: The `filter` scope MUST apply to export, import, inventory, and discovery operations in the `WorkItems` module and on `OrganisationEntry`
- **FR-011**: Skipped work items (filtered out) MUST produce a diagnostic log entry with level-appropriate detail — **Information**: field name and mode; **Debug**: adds the actual field value; **Trace**: adds the pattern. `ILogger.IsEnabled()` guards MUST be used to avoid string allocation at higher log levels.
- **FR-012**: During import, `filter` scope evaluation MUST use the field values from the **last revision** in the package for that work item; earlier revisions are not evaluated for filter purposes
- **FR-013**: When a valid `filter` scope results in zero items being processed, the run MUST complete successfully and MUST log a warning stating that zero items passed the filter; this is not treated as an error
- **FR-014**: Adding `scopes` to `OrganisationEntry` is a purely additive, non-breaking change; no `configVersion` bump or upgrader is required

### Key Entities

- **FilterScope**: A configuration entry with `mode` (`include`/`exclude`), `field` (string field reference), and `pattern` (regex string). Belongs to a `WorkItems` module's `scopes` array or an `OrganisationEntry`'s `scopes` array. Parsed into `WorkItemFieldFilterOptions` runtime predicates by `WorkItemsModuleExtensions.FromModule()`.
- **WiqlScope**: An existing scope type with a `query` parameter. Extended to be usable on `OrganisationEntry` in addition to its current home on module scopes. Flows as `WorkItemFetchScope.BaseQuery`.
- **OrganisationEntry.Scopes**: New property on the abstract base class — ordered list of `MigrationOptionsScope` entries (`wiql` and `filter` types) that refine which work items an organisation operates on during discovery/inventory.
- **ScopedOrganisationEndpoint.Scopes**: New property on the job-contract type — `IReadOnlyList<JobModuleScope>` carrying parsed org-level scopes from CLI to `InventoryService`.
- **WorkItemFieldFilterOptions** (runtime, from spec 015): `{ FieldName, Operator, Value }` — the runtime predicate type used by `IWorkItemFetchService` internally. Config-level filter scopes are parsed into this type by adding a `Regex` operator to `FilterOperator` and mapping `{ mode, field, pattern }` → `WorkItemFieldFilterOptions(field, Regex, pattern)`. The `include`/`exclude` mode is carried separately and applied at the `InventoryService` / `WorkItemExportOrchestrator` caller layer.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An export run with an area-path `include` filter produces a package containing only work items matching the filter, verifiable by comparing package item count against a WIQL count for the same area path
- **SC-002**: An export run with an area-path `exclude` filter produces a package with no work items matching the excluded pattern
- **SC-003**: An inventory run using a custom `wiql` scope on an organisation returns a work item count consistent with the custom query, not the full project count
- **SC-004**: A configuration with an invalid regex in a `filter` scope fails at startup (before any API calls are made) with a message identifying the invalid pattern
- **SC-005**: All existing export and import scenarios continue to pass unchanged when no `filter` scopes are configured (backward compatibility)
- **SC-006**: An inventory run with filter scopes configured produces a `WorkItemFetchScope` whose `Fields` array contains `System.Rev` unioned with the filter-referenced field names — verifiable by unit test asserting the scope passed to `IWorkItemFetchService.FetchAsync`. No additional API calls are made. Documentation MUST warn users to prefer short fields and minimise filter count.
- **SC-007**: A run where all items are filtered out logs a warning and exits with success — verified by inspecting the log output and the exit code
- **SC-008**: An inventory run with a `filter` scope on an organisation entry returns only work items matching the filter — verifiable by comparing against an inventory run without the filter scope

## Assumptions

- `filter` scopes apply to all operations — Export, Import, Inventory, and Discovery — wherever scopes are evaluated
- `Projects[]` on `OrganisationEntry` remains unchanged; it is project-level coarse selection and is a different abstraction from item-level `filter` scopes
- The regex engine uses a fixed internal 2-second timeout per evaluation to guard against catastrophic backtracking; this is not user-configurable. `RegexMatchTimeoutException` is caught by the filter evaluation layer, logged as a warning, and treated as a non-match (include = fail, exclude = pass); migration continues uninterrupted
- Field values are compared as their string representation; no type coercion is performed before regex matching
- During export, a pre-filter pass via `IWorkItemFetchService.FetchAsync` (fetching only filter-referenced fields) runs before revision history is fetched; filtered-out items incur no revision API calls
- During import, only the last revision's fields are evaluated by filter scopes; a lightweight pre-pass enumerates revision folder names only (no file reads) to identify the last folder per work item, then reads only those `revision.json` files before the main streaming pass. `IWorkItemFetchService` does not apply to the import path — it reads from the local package filesystem, not a source API
- Inventory and discovery filter evaluation flows through `IWorkItemFetchService.FetchAsync` via `WorkItemFetchScope.FilterOptions`. `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` accepts an optional `WorkItemFetchScope? scope = null`; the implementation merges `scope.Fields` with `["System.Rev"]` and passes the combined scope to `FetchAsync` — no extra API calls, just a slightly larger batch response
- `WorkItemFieldFilterOptions` (runtime predicate type from spec 015) is extended with a `Regex` operator in `FilterOperator`. Config-level `{ mode, field, pattern }` is parsed into `WorkItemFieldFilterOptions(field, Regex, pattern)`; the `include`/`exclude` mode is handled at the caller layer
- Org-level scopes (`wiql`, `filter`) are carried on `ScopedOrganisationEndpoint.Scopes` in the job contract; `InventoryService` reads them to build `WorkItemFetchScope` for each project
- The `wiql` scope on an organisation applies to inventory and discovery operations; it is not relevant to migration export/import, which uses the module-level `wiql` scope
- Adding `scopes` to `OrganisationEntry` is a non-breaking additive change — no `configVersion` bump or upgrader is required; existing configs without `scopes` continue to work unchanged
