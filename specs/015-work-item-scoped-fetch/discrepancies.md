# Architecture Discrepancies

**Feature**: Work Item Scoped Fetch Service (015-work-item-scoped-fetch)
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### `IWorkItemFetchService` not listed in mandatory reuse pattern

- **Source doc**: `docs/work-item-iteration-pattern.md`
- **Section**: "Mandatory Reuse Principle" / "New implementations MUST use existing architecture"
- **Issue**: The spec introduces `IWorkItemFetchService` as a new mandatory abstraction for all inventory/dependency/catalog callers, but `work-item-iteration-pattern.md` does not yet reference it. After this feature lands, any caller doing field-based work item fetching must use `IWorkItemFetchService` — this is not currently documented.
- **Suggested update**: Add a rule under the Mandatory Reuse Principle section:
  > "6. Use `IWorkItemFetchService` for field-projected, filtered work item fetching in inventory, dependency analysis, and catalog operations. Do not call `GetWorkItemsAsync` directly from these callers."
  Also add `IWorkItemFetchService` and `FetchedWorkItem` to the Overview list of abstractions.

### `FetchedWorkItem` and `IWorkItemFetchService` not referenced in `docs/modules.md`

- **Source doc**: `docs/modules.md`
- **Section**: Module Responsibilities / Contract Invariants
- **Issue**: `docs/modules.md` describes the `IModule` contract and storage rules but does not mention the new shared fetch service. After this feature lands, the modules doc should note that inventory/dependency modules use `IWorkItemFetchService` rather than direct API calls.
- **Suggested update**: Add a brief note to the Module Responsibilities table row for the inventory/dependency modules referencing `IWorkItemFetchService`.
