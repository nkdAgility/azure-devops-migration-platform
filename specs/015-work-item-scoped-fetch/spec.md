# Feature Specification: Work Item Scoped Fetch Service

**Feature Branch**: `015-work-item-scoped-fetch`  
**Created**: 2026-04-17  
**Status**: Draft  
**Depends on**: `016-organisation-endpoint` (OrganisationEndpoint must exist before this feature's interfaces can be defined)  
**Input**: User description: "IWorkItemFetchService — a new abstraction that sits above IWorkItemQueryWindowStrategy and below callers (Inventory, Dependency, Catalog). It streams work items with field projection and in-process filter evaluation, using OrganisationEndpoint (from feature 016) as its connection context. WorkItemFetchScope carries only the query scope — fields, filters, and optional base WIQL."

## Architecture References

| Document | Status |
|---|---|
| `docs/architecture.md` | Confirmed accurate — no changes required |
| `docs/module-development-guide.md` | Confirmed accurate — abstraction lives outside module layer |
| `docs/capabilities-guide.md` | Confirmed accurate — both ADO and TFS sources need implementations |
| `docs/work-item-iteration-guide.md` | **Discrepancy logged** — new abstraction must be registered in the mandatory reuse section |
| `.agents/guardrails/architecture-boundaries.md` | Confirmed accurate — new interface must live in `DevOpsMigrationPlatform.Abstractions` |

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Inventory counts work items with field projection and filters (Priority: P1)

As a migration operator running `devopsmigration discovery inventory`, I want the system to fetch only the fields required for counting and filtering — not the full work item payload — so that inventory scans complete faster and use less memory.

**Why this priority**: Inventory is the most frequently used operation. Reducing the data volume fetched per work item has the highest impact on user-perceived performance and reliability.

**Independent Test**: Can be fully tested by running `devopsmigration discovery inventory` against a project with 1,000+ work items and verifying that (a) only declared fields appear in the fetch requests, and (b) results are streamed without buffering the full set.

**Acceptance Scenarios**:

1. **Given** a project with work items of mixed types, **When** inventory runs with a `WorkItemFetchScope` specifying fields `["System.WorkItemType", "System.State"]`, **Then** only those fields are fetched per work item and the count is accurate.
2. **Given** a `WorkItemFetchScope` with a filter option restricting to type `Bug`, **When** inventory runs, **Then** only `Bug` items are counted; other types are discarded in-process without being written to any store.
3. **Given** 20,000+ work items in a project, **When** inventory streams via `IWorkItemFetchService`, **Then** memory usage remains bounded (no full result set in memory at any point).

---

### User Story 2 — Dependency analysis pre-filters by field before fetching relations (Priority: P2)

As a migration operator running `devopsmigration discovery dependencies`, I want the system to evaluate field-based filters before fetching relationship data, so that the expensive Relations expand call is only made for items that pass the filter.

**Why this priority**: Relation fetches are significantly more expensive than field fetches. Pre-filtering reduces API quota consumption and improves run time.

**Independent Test**: Can be fully tested by running a dependency scan on a project where only 10% of work items match the configured type/state filter, and confirming that Relations calls are made only for those 10%.

**Acceptance Scenarios**:

1. **Given** a project where 90% of items are type `Task` and a filter restricts to `Epic`, **When** dependency analysis runs, **Then** Relations expand calls are made only for `Epic` items.
2. **Given** an item that does not match field filters, **When** `IWorkItemFetchService.FetchAsync` is called, **Then** the item is not yielded to the caller.
3. **Given** an item that passes field filters, **When** dependency analysis needs its relations, **Then** the caller fetches relations separately for only that item — `IWorkItemFetchService` is not responsible for relation expansion.

---

### User Story 3 — TFS source returns field-projected work items via the same interface (Priority: P3)

As a migration operator using a TFS source, I want `IWorkItemFetchService` to have a TFS-backed implementation so that inventory and dependency analysis work uniformly regardless of source type.

**Why this priority**: Uniformity prevents per-caller special-casing of TFS vs ADO sources.

**Independent Test**: Can be fully tested by running inventory against a TFS source and confirming the same interface contract is exercised.

**Acceptance Scenarios**:

1. **Given** a TFS source configured in the job, **When** `IWorkItemFetchService.FetchAsync` is called, **Then** items are streamed back with the requested fields populated.
2. **Given** a TFS source with a filter option, **When** `FetchAsync` runs, **Then** items not matching the filter are excluded in-process before being yielded.

---

### Edge Cases

- What happens when `fields` is an empty list? → Service must reject the request with a clear error; fetching zero fields returns no usable data.
- What happens when a requested field does not exist on a work item type? → The field is omitted from the result without error; callers must handle missing keys.
- How does the service handle transient API failures mid-stream? → Propagates the exception to the caller; no partial result buffering occurs; caller is responsible for retry/resumption via checkpoint.
- What happens when `filterOptions` is null or empty? → All fetched items are yielded (no filter applied); this is the default pass-through behaviour.
- What happens when `baseQuery` produces zero IDs? → `FetchAsync` returns immediately with an empty sequence; no batch calls are made.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose `IWorkItemFetchService` as a new interface in `DevOpsMigrationPlatform.Abstractions`.
- **FR-002**: `IWorkItemFetchService.FetchAsync` MUST accept an `OrganisationEndpoint` (from feature 016), a `string project` name, a `WorkItemFetchScope` (query scope only), and a `CancellationToken`. Org URL and authentication MUST NOT be passed as separate parameters.
- **FR-003**: `FetchAsync` MUST stream results as `IAsyncEnumerable<FetchedWorkItem>` — one item at a time — without buffering the full result set in memory.
- **FR-004**: The service MUST evaluate `filterOptions` in-process after the batch fetch; items that do not satisfy all filter conditions MUST NOT be yielded.
- **FR-005**: The service MUST use `IWorkItemQueryWindowStrategy` internally to obtain work item IDs via date-windowed WIQL queries.
- **FR-006**: `FetchedWorkItem` MUST be an immutable record type containing the work item ID and an `IReadOnlyDictionary<string, object?>` of the fetched fields.
- **FR-007**: The ADO Services implementation MUST batch-fetch work items using only the declared fields (no full-payload fetches).
- **FR-008**: The TFS implementation MUST provide a functional stub that returns fields from the TFS Object Model store — it MUST NOT throw `NotImplementedException`.
- **FR-009**: `AzureDevOpsWorkItemDiscoveryService` MUST be refactored to delegate its inner batch loop to `IWorkItemFetchService` instead of calling `GetWorkItemsAsync` directly.
- **FR-010**: `AzureDevOpsDependencyAnalysisService` MUST be refactored to use `IWorkItemFetchService` for field-based pre-filtering; Relations expansion MUST remain a separate call performed only for items that pass the filter.
- **FR-011**: `CatalogService` MUST be updated to delegate through `IWorkItemFetchService` transitively (passthrough change — no logic change).
- **FR-012**: The `WorkItemExportOrchestrator` export path MUST NOT be changed; `FetchAsync` is for inventory/dependency/catalog callers only.
- **FR-013**: The interface MUST NOT accept `WorkItemExpand` as a parameter; relation expansion remains the caller's responsibility.
- **FR-014**: All callers MUST propagate `CancellationToken` through to `FetchAsync`.

### Key Entities

- **`OrganisationEndpoint`**: Defined in feature 016. Immutable record in `DevOpsMigrationPlatform.Abstractions` with `ResolvedUrl`, `Type`, and `Authentication` (`OrganisationEndpointAuthentication`). Used as the connection context parameter for `FetchAsync`.
- **`IWorkItemFetchService`**: New interface in `DevOpsMigrationPlatform.Abstractions`. Signature: `FetchAsync(OrganisationEndpoint endpoint, string project, WorkItemFetchScope scope, CancellationToken ct)`.
- **`WorkItemFetchScope`**: Immutable value object (record) in `DevOpsMigrationPlatform.Abstractions` that encapsulates query-scope concerns only: required field names (`IReadOnlyList<string>`), optional filter options (`IReadOnlyList<WorkItemFieldFilterOptions>?`), and optional base WIQL `WHERE` clause. Connection details are NOT part of this object.
- **`FetchedWorkItem`**: Immutable record: `int Id` + `IReadOnlyDictionary<string, object?> Fields`. Lives in `DevOpsMigrationPlatform.Abstractions`.
- **`WorkItemFieldFilterOptions`**: Filter descriptor (defined by feature 014). Evaluated in-process within `FetchAsync` against the fetched fields.
- **`AzureDevOpsWorkItemFetchService`**: ADO Services implementation in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`. Uses `IWorkItemQueryWindowStrategy` + batch field fetch.
- **`TfsWorkItemFetchService`**: TFS implementation stub in the TFS infrastructure layer. Returns fields from TFS Object Model without buffering.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Inventory scans complete without loading all work item field data into memory simultaneously; peak memory during a 20,000-item scan does not increase materially compared to the baseline before this change.
- **SC-002**: All existing callers (`AzureDevOpsWorkItemDiscoveryService`, `AzureDevOpsDependencyAnalysisService`, `CatalogService`) have zero direct calls to `GetWorkItemsAsync` for field-fetch purposes after the refactor.
- **SC-003**: Feature 014 filter scopes integrate by passing `filterOptions` to `FetchAsync` — no per-caller wiring changes are required beyond that.
- **SC-004**: The full test suite passes after the refactor with no regressions.
- **SC-005**: Both ADO and TFS inventory operations exercise the same `IWorkItemFetchService` interface contract without source-type branching in callers.

## Assumptions

- `WorkItemFieldFilterOptions` (from feature 014) is defined in `DevOpsMigrationPlatform.Abstractions` before or alongside this feature; if unavailable, a placeholder stub will be used and replaced when 014 lands.
- The TFS implementation stub is not required to perform date-windowed WIQL — it may use a simpler query strategy appropriate to the TFS Object Model, documented in the TFS exporter spec.
- `IWorkItemQueryWindowStrategy` already exists and is used internally by the ADO implementation; this feature does not change its interface (that is done by feature 016).
- The `WorkItemExportOrchestrator` path (full revision export) is explicitly out of scope for this abstraction — it has its own source stream (`IWorkItemRevisionSource`).
- Relations expansion is intentionally excluded from `IWorkItemFetchService`; this is a deliberate architectural constraint, not an oversight.
- This feature is a prerequisite for feature 014 field-filter scopes; both may be developed in parallel but 015 must land first (or concurrently) to unblock the per-caller wiring in 014.
- Feature 016 (`OrganisationEndpoint`) must land before or alongside this feature so that `FetchAsync` can accept `OrganisationEndpoint` in its signature.
