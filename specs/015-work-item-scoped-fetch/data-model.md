# Data Model: Work Item Scoped Fetch Service

**Feature**: 015-work-item-scoped-fetch  
**Date**: 2026-04-18

## Entities

### WorkItemFetchScope

**Type**: Immutable record  
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/WorkItemFetchScope.cs`  
**Purpose**: Encapsulates query-scope concerns for `IWorkItemFetchService.FetchAsync` — which fields to project, which filters to apply, and an optional base WIQL WHERE clause.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Fields` | `IReadOnlyList<string>` | Yes | Field reference names to project (e.g., `"System.WorkItemType"`, `"System.State"`). Must not be empty. |
| `FilterOptions` | `IReadOnlyList<WorkItemFieldFilterOptions>?` | No | Optional in-process filter predicates. Null or empty = all items pass. |
| `BaseQuery` | `string?` | No | Optional WIQL WHERE clause fragment appended to the window strategy's query. Null = no additional constraint. |

**Validation rules**:
- `Fields` must not be null or empty (FR: empty fields → reject with error).
- Each field name must be a non-empty string.
- `FilterOptions` may be null (pass-through behaviour).
- `BaseQuery` may be null (no additional WIQL constraint).

**State transitions**: None — immutable value object.

### FetchedWorkItem

**Type**: Immutable record  
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/FetchedWorkItem.cs`  
**Purpose**: Represents a single work item with only the requested field values. Yielded one-at-a-time from `FetchAsync`.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Id` | `int` | Yes | The work item ID in the source system. |
| `Fields` | `IReadOnlyDictionary<string, object?>` | Yes | Fetched field values keyed by reference name. Missing fields are omitted (not null-valued). |

**Validation rules**:
- `Id` must be > 0.
- `Fields` must not be null (may be empty if all requested fields are missing on the item).

**State transitions**: None — immutable value object.

### WorkItemFieldFilterOptions (Placeholder — Feature 014)

**Type**: Immutable record  
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/WorkItemFieldFilterOptions.cs`  
**Purpose**: Describes a single field-based filter predicate. Placeholder until feature 014 provides the full implementation.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `FieldName` | `string` | Yes | The field reference name to filter on (e.g., `"System.WorkItemType"`). |
| `Operator` | `FilterOperator` | Yes | Comparison operator (e.g., `Equals`, `NotEquals`, `Contains`). |
| `Value` | `object?` | Yes | The value to compare against. |

**Validation rules**:
- `FieldName` must not be null or empty.
- `Operator` must be a valid `FilterOperator` enum value.
- `Value` may be null (for `IsNull` / `IsNotNull` operators).

### FilterOperator (Placeholder — Feature 014)

**Type**: Enum  
**Location**: `DevOpsMigrationPlatform.Abstractions/Models/FilterOperator.cs`  
**Purpose**: Supported filter comparison operators.

| Value | Description |
|-------|-------------|
| `Equals` | Exact match (case-insensitive for strings). |
| `NotEquals` | Inverse of Equals. |
| `Contains` | Substring match (strings only). |

## Relationships

```
IWorkItemFetchService
    ├── accepts: OrganisationEndpoint (feature 016, already exists)
    ├── accepts: WorkItemFetchScope
    │       ├── contains: IReadOnlyList<string> Fields
    │       ├── contains: IReadOnlyList<WorkItemFieldFilterOptions>? FilterOptions
    │       │       └── uses: FilterOperator enum
    │       └── contains: string? BaseQuery
    └── yields: IAsyncEnumerable<FetchedWorkItem>
            ├── int Id
            └── IReadOnlyDictionary<string, object?> Fields

AzureDevOpsWorkItemFetchService (implements IWorkItemFetchService)
    ├── depends on: IWorkItemQueryWindowStrategy (existing)
    ├── depends on: IAzureDevOpsClientFactory (existing)
    └── internally: batch-fetches with field projection via WorkItemTrackingHttpClient

TfsWorkItemFetchService (implements IWorkItemFetchService)
    └── depends on: TFS Object Model WorkItemStore (net481 target)
```

## Interface Contract

### IWorkItemFetchService

**Location**: `DevOpsMigrationPlatform.Abstractions/Services/IWorkItemFetchService.cs`

```
FetchAsync(
    endpoint: OrganisationEndpoint,
    project: string,
    scope: WorkItemFetchScope,
    cancellationToken: CancellationToken
) → IAsyncEnumerable<FetchedWorkItem>
```

**Behavioural contract**:
1. Uses `IWorkItemQueryWindowStrategy` internally to obtain work item IDs via date-windowed WIQL.
2. Batch-fetches work items (up to 200 per batch) with only the declared `scope.Fields`.
3. Evaluates `scope.FilterOptions` in-process after fetch; non-matching items are not yielded.
4. Streams results one item at a time — no full result set buffering.
5. Propagates `CancellationToken` to all internal async operations.
6. Does NOT perform Relations expansion (caller responsibility per FR-013).
7. Returns immediately with empty sequence when window strategy yields zero IDs.
