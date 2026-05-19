# Architecture Discrepancies

**Feature**: Work Item Scoped Fetch Service (015-work-item-scoped-fetch)
**Flagged by**: speckit.specify, speckit.plan
**Status**: All resolved

## Discrepancies

### D-001: `IWorkItemFetchService` not listed in mandatory reuse pattern

- **Source doc**: `docs/work-item-iteration-guide.md`
- **Section**: "Mandatory Reuse Principle" / "New implementations MUST use existing architecture"
- **Issue**: The spec introduces `IWorkItemFetchService` as a new mandatory abstraction for all inventory/dependency/catalog callers, but `work-item-iteration-pattern.md` does not yet reference it. After this feature lands, any caller doing field-based work item fetching must use `IWorkItemFetchService` — this is not currently documented.
- **Suggested update**: Add a rule under the Mandatory Reuse Principle section:
  > "6. Use `IWorkItemFetchService` for field-projected, filtered work item fetching in inventory, dependency analysis, and catalog operations. Do not call `GetWorkItemsAsync` directly from these callers."
  Also add `IWorkItemFetchService` and `FetchedWorkItem` to the Overview list of abstractions.
- **Status**: ✓ Resolved in speckit.implement: `FetchedWorkItem` and `IWorkItemFetchService` not referenced in `docs/module-development-guide.md`

- **Source doc**: `docs/module-development-guide.md`
- **Section**: Module Responsibilities / Contract Invariants
- **Issue**: `docs/module-development-guide.md` describes the `IModule` contract and storage rules but does not mention the new shared fetch service. After this feature lands, the modules doc should note that inventory/dependency modules use `IWorkItemFetchService` rather than direct API calls.
- **Suggested update**: Add a brief note to the Module Responsibilities table row for the inventory/dependency modules referencing `IWorkItemFetchService`.
- **Status**: ✓ Resolved in speckit.implement

### D-003: `docs/architecture.md` does not mention `IWorkItemFetchService` in the OrganisationEndpoint section

- **Source doc**: `docs/architecture.md`
- **Section**: OrganisationEndpoint — Canonical Connection Context
- **Issue**: The architecture overview mentions `OrganisationEndpoint` as the immutable connection context type but does not yet list `IWorkItemFetchService` among its consumers. After this feature lands, `FetchAsync(OrganisationEndpoint, ...)` becomes a primary consumer of this type.
- **Suggested update**: Add `IWorkItemFetchService` to the list of service interfaces that use `OrganisationEndpoint` as their connection context parameter.
- **Status**: ✓ Resolved in speckit.implement

### D-004: `IWorkItemQueryWindowStrategy` still accepts `MigrationEndpointOptions` — not yet aligned with `OrganisationEndpoint`

- **Source doc**: `.agents/20-guardrails/core/architecture-boundaries.md` (rule 21 — mandatory reuse), `docs/architecture.md` (OrganisationEndpoint section)
- **Section**: Interface signatures
- **Issue**: The existing `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync()` originally accepted `MigrationEndpointOptions` (a config-layer type). The new `IWorkItemFetchService.FetchAsync()` accepts `OrganisationEndpoint` (the resolved connection type).
- **Suggested update**: Align `IWorkItemQueryWindowStrategy` signature to accept `OrganisationEndpoint`.
- **Status**: ✓ Resolved — `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync` was updated to accept `OrganisationEndpoint` directly. Adapter classes (`AzureDevOpsEndpointOptionsAdapter`, `TfsMigrationEndpointOptionsAdapter`) were deleted. `MigrationEndpointOptions` now has an abstract `ToOrganisationEndpoint()` method that each concrete type implements.

