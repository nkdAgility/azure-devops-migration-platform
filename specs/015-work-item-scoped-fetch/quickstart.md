# Quickstart: Work Item Scoped Fetch Service

**Feature**: 015-work-item-scoped-fetch

## What This Feature Does

`IWorkItemFetchService` is a new abstraction that provides field-projected, filtered work item fetching for inventory, dependency analysis, and catalog operations. It sits between `IWorkItemQueryWindowStrategy` (which handles date-windowed WIQL queries) and callers like discovery and dependency services.

## Key Concepts

1. **Field Projection** — Fetch only the fields you need, not full work item payloads.
2. **In-Process Filtering** — Apply field-based filter predicates after fetch, before yielding results.
3. **Streaming** — Results stream one item at a time via `IAsyncEnumerable<FetchedWorkItem>`.
4. **Source-Agnostic** — Same interface works for ADO REST and TFS Object Model sources.

## Usage Example (Caller Perspective)

```csharp
// Define what you need
var scope = new WorkItemFetchScope(
    Fields: new[] { "System.WorkItemType", "System.State", "System.Title" },
    FilterOptions: new[]
    {
        new WorkItemFieldFilterOptions("System.WorkItemType", FilterOperator.Equals, "Bug")
    });

// Stream matching items
await foreach (var item in _fetchService.FetchAsync(endpoint, "MyProject", scope, ct))
{
    // item.Id is the work item ID
    // item.Fields contains only the requested fields
    var type = item.Fields["System.WorkItemType"]?.ToString();
    var state = item.Fields["System.State"]?.ToString();
}
```

## Implementation Checklist

1. Create `WorkItemFetchScope`, `FetchedWorkItem`, `WorkItemFieldFilterOptions` records in Abstractions
2. Create `IWorkItemFetchService` interface in Abstractions
3. Implement `AzureDevOpsWorkItemFetchService` in Infrastructure.AzureDevOps
4. Implement `TfsWorkItemFetchService` in Infrastructure (multi-targeted)
5. Refactor `AzureDevOpsWorkItemDiscoveryService` to use `IWorkItemFetchService`
6. Refactor `AzureDevOpsDependencyAnalysisService` to use `IWorkItemFetchService`
7. Register `IWorkItemFetchService` in DI extension methods
8. Update `docs/work-item-iteration-guide.md` and `docs/module-development-guide.md`

## Architecture Constraints

- Interface lives in `DevOpsMigrationPlatform.Abstractions`
- No `WorkItemExpand` parameter — Relations expansion is caller's responsibility
- No checkpointing — callers manage their own cursors
- No retry logic — HTTP pipeline handles resilience
- CancellationToken must be propagated throughout
- Empty `Fields` list must be rejected with `ArgumentException`
