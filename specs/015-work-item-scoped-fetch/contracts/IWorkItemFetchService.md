# Contract: IWorkItemFetchService

**Feature**: 015-work-item-scoped-fetch  
**Type**: Public interface in `DevOpsMigrationPlatform.Abstractions`

## Interface Signature

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Streams work items with field projection and in-process filter evaluation.
/// Sits above IWorkItemQueryWindowStrategy and below callers (Inventory, Dependency, Catalog).
/// </summary>
public interface IWorkItemFetchService
{
    /// <summary>
    /// Fetches work items from the source system with only the declared fields,
    /// applying in-process filters before yielding each item.
    /// </summary>
    /// <param name="endpoint">Resolved connection context (OrganisationEndpoint from feature 016).</param>
    /// <param name="project">Target project name.</param>
    /// <param name="scope">Query scope: required fields, optional filters, optional base WIQL WHERE clause.</param>
    /// <param name="cancellationToken">Cancellation token — must be propagated to all internal async operations.</param>
    /// <returns>
    /// An asynchronous stream of fetched work items. Each item contains only the requested fields.
    /// Items that do not satisfy filterOptions are excluded.
    /// Empty when the underlying query returns zero IDs.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when scope.Fields is null or empty.</exception>
    IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default);
}
```

## Behavioural Contract

### Streaming guarantee
- Results are yielded one item at a time via `IAsyncEnumerable<FetchedWorkItem>`.
- No internal list, array, or collection accumulates the full result set.
- Memory usage is bounded regardless of the number of matching work items.

### Field projection
- Only the fields listed in `scope.Fields` are requested from the source API.
- The source API implementation (ADO REST, TFS OM) must use field-specific fetch calls — not full-payload fetches.
- If a requested field does not exist on a work item type, it is omitted from the `FetchedWorkItem.Fields` dictionary — no error is thrown.

### In-process filtering
- If `scope.FilterOptions` is null or empty, all fetched items are yielded (pass-through).
- If `scope.FilterOptions` contains entries, each fetched item is evaluated against ALL filter predicates.
- An item is yielded only if it satisfies ALL filter conditions (AND semantics).
- Filtering happens after the batch fetch, before `yield return`.

### Cancellation
- `CancellationToken` must be forwarded to:
  - `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync()`
  - All HTTP client calls
  - All batch-processing loops (check at batch boundaries)
- `OperationCanceledException` propagates unmodified to the caller.

### Error handling
- `ArgumentException` for empty/null `scope.Fields` — fail fast before any API call.
- Transient API failures propagate to the caller as exceptions — no partial result buffering.
- The service does not implement retry logic (caller or HTTP pipeline responsibility per resilience guardrail).

### What this service does NOT do
- Does NOT perform Relations expansion (`WorkItemExpand.Relations`) — FR-013.
- Does NOT accept `WorkItemExpand` as a parameter — FR-013.
- Does NOT perform checkpointing or progress reporting — caller responsibility.
- Does NOT write to any store — read-only fetch operation.

## Callers

| Caller | Usage |
|--------|-------|
| `AzureDevOpsWorkItemDiscoveryService` | Replaces direct `GetWorkItemsAsync` call in the batch loop |
| `AzureDevOpsDependencyAnalysisService` | Pre-filters items before Relations expansion |
| `CatalogService` | Transitively updated (delegates through discovery) |

## Implementations

| Implementation | Project | Notes |
|----------------|---------|-------|
| `AzureDevOpsWorkItemFetchService` | `Infrastructure.AzureDevOps` | Uses `IWorkItemQueryWindowStrategy` + `IAzureDevOpsClientFactory` |
| `TfsWorkItemFetchService` | `Infrastructure` (multi-targeted) or `Infrastructure.TFS` | Uses TFS Object Model `WorkItemStore.Query()` |
