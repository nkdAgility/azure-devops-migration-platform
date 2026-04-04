# Data Model: Work Items Inventory Command

**Phase 1 output for feature 003-inventory-workitems**

---

## New types (net481 + net10.0 unless noted)

### `EndpointAuthenticationOptions` (Abstractions)

Replaces the missing auth section in `MigrationEndpointOptions`.

| Property | Type | Notes |
|---|---|---|
| `Type` | `string` | `"Pat"` or `"Windows"` |
| `AccessToken` | `string?` | Literal value or `$ENV:VARNAME` prefix. Resolved by `TokenResolver`. |

Validation: for `Pat`, resolved token must be non-empty after resolution.

---

### `MigrationEndpointOptions` — extended (Abstractions)

Add one property to the existing class:

| New Property | Type | Notes |
|---|---|---|
| `Authentication` | `EndpointAuthenticationOptions?` | Optional for backwards compat; required for inventory and new export/import |

---

### `OrganisationEntry` (Abstractions)

One entry in the `organisations` list.

| Property | Type | Default | Notes |
|---|---|---|---|
| `Type` | `string` | required | `"AzureDevOpsServices"` or `"TeamFoundationServer"` |
| `OrgOrCollection` | `string` | required | Org URL or collection URL |
| `Projects` | `List<string>` | `[]` | Empty = all projects |
| `ApiVersion` | `string?` | — | Pinned API version |
| `Authentication` | `EndpointAuthenticationOptions` | required | Auth block |
| `Enabled` | `bool` | `true` | `false` = skip silently |

---

### `InventoryOptions` (Abstractions)

Root options class for the inventory command. Bound to the config root. Exactly one of `Source` or `Organisations` must be set.

| Property | Type | Notes |
|---|---|---|
| `ConfigVersion` | `string` | `"1.0"` |
| `Source` | `MigrationEndpointOptions?` | Mode 1 |
| `Organisations` | `List<OrganisationEntry>?` | Mode 2 |

Validation (run at startup):
- Both set → error.
- Neither set → error.
- Mode 1, `Source.Project` null/empty, `--all-projects` not passed → error.
- Mode 2, list empty → error.
- Mode 2, any enabled entry missing `Type` or `OrgOrCollection` → error.
- Mode 1, `Pat` auth, resolved token empty → error.

---

### `InventorySummary` (Abstractions)

Per-project result record. Written to the CSV on completion.

| Property | Type | Notes |
|---|---|---|
| `OrgOrCollection` | `string` | Source org/collection |
| `ProjectName` | `string` | Project name |
| `WorkItemsCount` | `int` | Total work items found |
| `RevisionsCount` | `int` | Sum of all `System.Rev` values |
| `ReposCount` | `int` | Always 0 in this feature |
| `PipelinesCount` | `int` | Always 0 in this feature |
| `IsComplete` | `bool` | All windows scanned without error |
| `Error` | `string?` | Set if counting failed for this project |
| `LastUpdatedUtc` | `DateTime` | UTC time of last update |

---

### `InventoryProgressEvent` (Abstractions)

Emitted by `IProgressSink` on each window completion. Used by both the in-process ADO path and the TFS subprocess NDJSON path.

| Property | Type | Notes |
|---|---|---|
| `ProjectName` | `string` | |
| `OrgOrCollection` | `string` | |
| `WorkItemsCount` | `int` | Running total |
| `RevisionsCount` | `int` | Running total |
| `IsComplete` | `bool` | True on the final event for this project |
| `WindowStart` | `DateTime` | Window that was just counted |
| `WindowEnd` | `DateTime` | |
| `WindowSize` | `TimeSpan` | Current window size (diagnostic) |
| `Error` | `string?` | Set on error events |
| `Timestamp` | `DateTime` | UTC |

---

### `TokenResolver` (Abstractions, static utility)

```csharp
public static class TokenResolver
{
    public static string? Resolve(string? raw);
}
```

- Returns `null` if `raw` is null or empty.
- If `raw` starts with `$ENV:`, reads that env var. Throws `InvalidOperationException` if unset/empty.
- Otherwise returns `raw` unchanged.

Compiled for both `net481` and `net10.0`.

---

### `IInventoryService` (Abstractions)

New service interface for work item counting. Implemented by `AzureDevOpsInventoryService` (net10.0, `Infrastructure.AzureDevOps`).

```csharp
public interface IInventoryService
{
    IAsyncEnumerable<InventoryProgressEvent> CountWorkItemsAsync(
        string orgOrCollection,
        string project,
        string pat,
        CancellationToken cancellationToken = default);
}
```

---

## Existing types modified

| Type | Location | Change |
|---|---|---|
| `MigrationEndpointOptions` | `Abstractions` | Add `Authentication` property |
| `CatalogService` | `Infrastructure.AzureDevOps` | Date-window counting replaces ID-cursor loop |
| `ICatalogService` | `Abstractions/Services` | No change to interface; implementation changes internally |
| `InventoryCommand` | `CLI.Migration/Commands/Discovery` | Full rewrite: remove `AzureDevOpsSettings` base, add `--all-projects`, read config via `IOptions<InventoryOptions>` |
| `AzureDevOpsSettings` | `CLI.Migration/Commands` | Deprecated and removed (violations: bare org URL + PAT as CLI args) |
| `Program.cs` | `CLI.Migration` | Register `InventoryOptions`, `IInventoryService` |
| `ExportCommand` (TfsMigration) | `CLI.TfsMigration/Commands` | No change; inventory is a parallel subcommand |

---

## New files summary

| File | Project | Purpose |
|---|---|---|
| `Options/EndpointAuthenticationOptions.cs` | Abstractions | Auth block model |
| `Options/OrganisationEntry.cs` | Abstractions | `organisations` list entry |
| `Options/InventoryOptions.cs` | Abstractions | Root options for inventory command |
| `Models/InventorySummary.cs` | Abstractions | Per-project result |
| `Models/InventoryProgressEvent.cs` | Abstractions | Progress event for inventory |
| `Utilities/TokenResolver.cs` | Abstractions | `$ENV:VARNAME` resolution |
| `Services/IInventoryService.cs` | Abstractions | Inventory service interface |
| `Services/AzureDevOpsInventoryService.cs` | Infrastructure.AzureDevOps | Date-window counting implementation |
| `Commands/Discovery/TfsInventoryProcessAdapter.cs` | CLI.Migration | Spawns TFS subprocess with `inventory` subcommand |
| `Commands/InventoryCommand.cs` (TfsMigration) | CLI.TfsMigration | `inventory` subcommand entry point |
| `TfsInventoryAgent.cs` | CLI.TfsMigration | Parallel of `TfsExportAgent`; uses `WorkItemStoreExtensions` |
