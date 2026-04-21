# Contract: IWorkItemImportTarget — additions

**Namespace**: `DevOpsMigrationPlatform.Abstractions`  
**Existing interface**: `IWorkItemImportTarget`  
**Implementations to update**: `AzureDevOpsWorkItemImportTarget`, `SimulatedWorkItemImportTarget`

## New method

```csharp
/// <summary>
/// Returns true if a work item with <paramref name="targetWorkItemId"/> exists in the target project.
/// Used by the integrity check and Stage A deleted-target guard.
/// Must NOT throw for 404 responses — return false instead.
/// </summary>
Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct);
```

## Implementation notes

### `AzureDevOpsWorkItemImportTarget`

Calls `WorkItemTrackingHttpClient.GetWorkItemAsync(id, cancellationToken: ct)`.  
If the response throws `VssServiceException` with status 404 → return `false`.  
All other exceptions propagate normally.

### `SimulatedWorkItemImportTarget`

Returns `true` always. The simulated target never deletes work items.  
Allows test scenarios to override via a configurable `DeletedIds` set if needed.

## Breaking change impact

All `IWorkItemImportTarget` implementations must be updated. Current implementations:
- `AzureDevOpsWorkItemImportTarget` (Infrastructure.AzureDevOps)
- `SimulatedWorkItemImportTarget` (Infrastructure.Simulated)

Any mock-based test that creates `Mock<IWorkItemImportTarget>` with `MockBehavior.Strict` must add setup for `WorkItemExistsAsync`.
