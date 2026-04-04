# Data Model: Inventory Command — Config-Driven, Multi-Source, Paginated

**Feature Branch**: `003-inventory-command`  
**Phase**: 1 — Design & Contracts  
**Status**: Complete

---

## Entity Overview

```
InventoryOptions (config root)
  └── Sources : List<InventorySourceOptions>   [1..*]

InventoryCommand (Spectre.Console command)
  ├── injects IOptions<InventoryOptions>
  ├── injects ITokenResolver
  ├── injects ICatalogService                  (AzureDevOps path)
  └── injects ExternalToolRunner               (TFS path, static class)

ICatalogService.CountAllWorkItemsAsync()
  └── yields ProjectDiscoverySummary           (already in Abstractions)

InventoryResult (aggregate of all sources)
  └── SourceResults : List<InventorySourceResult>   [1..*]

InventorySourceResult (result of one source)
  └── ProjectSummaries : List<ProjectDiscoverySummary>   [0..*]
```

---

## Configuration Entities

### `InventoryOptions`

**Location**: `DevOpsMigrationPlatform.Abstractions/Options/InventoryOptions.cs`  
**Bound from**: `configuration.GetSection("inventory")` — i.e., the `inventory` key in `migration.json`  
**Config version requirement**: `configVersion` must be `"2.0"` when `inventory` section is present

```csharp
/// <summary>
/// Top-level options for the <c>discovery inventory</c> command.
/// Sealed to prevent subclassing. Bound from the <c>inventory</c> section of migration.json.
/// </summary>
public sealed class InventoryOptions
{
    public const string SectionName = "inventory";

    /// <summary>One or more source organisations/collections to inventory.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one source is required in inventory.sources.")]
    public List<InventorySourceOptions> Sources { get; set; } = [];
}
```

**Validation**:
- `Sources` must not be null or empty (`[Required]` + `[MinLength(1)]`).
- Each `InventorySourceOptions` entry is validated independently (see below).
- Registered via `ValidateDataAnnotations().ValidateOnStart()`.

---

### `InventorySourceOptions`

**Location**: `DevOpsMigrationPlatform.Abstractions/Options/InventorySourceOptions.cs`

```csharp
/// <summary>
/// A single connection target for the inventory command.
/// One entry per organisation or TFS collection.
/// </summary>
public sealed class InventorySourceOptions
{
    /// <summary>
    /// Source type. Must be "AzureDevOpsServices" or "TeamFoundationServer".
    /// </summary>
    [Required]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Organisation URL (Azure DevOps Services) or collection URL (TFS).
    /// Example: https://dev.azure.com/myorg  or  http://tfs:8080/tfs/DefaultCollection
    /// </summary>
    [Required]
    public string OrgOrCollection { get; set; } = string.Empty;

    /// <summary>
    /// Optional team project name. When set, only this project is inventoried.
    /// When omitted, all projects in the organisation/collection are discovered.
    /// </summary>
    public string? Project { get; set; }

    /// <summary>
    /// PAT or "$ENV:VARNAME" reference.
    /// Resolved at runtime by ITokenResolver before any API call is made.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// REST API version to request. Defaults to "7.1" when not specified.
    /// </summary>
    public string? ApiVersion { get; set; }
}
```

**Valid `Type` values** (matches existing `MigrationEndpointOptions.Type` convention):
- `"AzureDevOpsServices"` — uses REST API via `ICatalogService`
- `"TeamFoundationServer"` — delegates to `ExternalToolRunner` subprocess

---

## Token Resolution Entity

### `ITokenResolver`

**Location**: `DevOpsMigrationPlatform.Abstractions/Services/ITokenResolver.cs`

```csharp
/// <summary>
/// Resolves a raw token string. If the value begins with "$ENV:", reads the
/// remainder as an environment variable name and returns that variable's value.
/// Returns the input unchanged for plain (non-"$ENV:") values.
/// Throws <see cref="InvalidOperationException"/> when the referenced
/// environment variable is not set.
/// </summary>
public interface ITokenResolver
{
    string Resolve(string rawToken);
}
```

**Resolution rules**:

| Input | Behaviour |
|---|---|
| `"mysecretpat123"` | Returned as-is |
| `"$ENV:ADO_PAT"` | Returns `Environment.GetEnvironmentVariable("ADO_PAT")` |
| `"$ENV:"` (empty variable name) | Throws `InvalidOperationException`: `"Malformed token reference '$ENV:' — variable name is missing."` |
| `"$ENV:MISSING_VAR"` when not set | Throws `InvalidOperationException`: `"Environment variable 'MISSING_VAR' referenced in config token is not set."` |

**Placement rationale**: `ITokenResolver` in Abstractions + `TokenResolver` implementation in Infrastructure ensures reusability by any future command with a `token` field (FR-012).

---

## Result Entities

### `InventorySourceResult`

**Location**: `DevOpsMigrationPlatform.Abstractions/Models/InventorySourceResult.cs`

```csharp
/// <summary>
/// The aggregate output of running inventory against one source entry.
/// </summary>
public sealed class InventorySourceResult
{
    /// <summary>Source label — the orgOrCollection URL.</summary>
    public string SourceLabel { get; init; } = string.Empty;

    /// <summary>Project-level summaries for this source.</summary>
    public IReadOnlyList<ProjectDiscoverySummary> ProjectSummaries { get; init; }
        = Array.Empty<ProjectDiscoverySummary>();

    /// <summary>
    /// Non-null when this source failed. Contains a human-readable description
    /// identifying the source and the failure type.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when ErrorMessage is null and at least the source was reachable.</summary>
    public bool Succeeded => ErrorMessage is null;
}
```

### `ProjectDiscoverySummary` (existing — no changes)

**Location**: `DevOpsMigrationPlatform.Abstractions/Models/ProjectDiscoverySummary.cs`  
Existing model yielded by `ICatalogService.CountAllWorkItemsAsync`. Used as-is.

| Field | Type | Description |
|---|---|---|
| `ProjectName` | `string` | Team project name |
| `WorkItemsCount` | `int` | Accumulated work item count (updated per pagination page) |
| `RevisionsCount` | `int` | Accumulated revision count (optional; inventory focuses on `WorkItemsCount`) |
| `IsWorkItemComplete` | `bool` | `true` when the last page had fewer than 20 000 items (pagination done) |
| `LastUpdatedUtc` | `DateTime` | Timestamp of last update |

---

## TFS Subprocess Protocol Entities

### `TfsInventoryRequest`

**Location**: `DevOpsMigrationPlatform.Abstractions/Models/TfsInventoryRequest.cs`  
**Multi-targeted**: `net481;net10.0` (defined in Abstractions)

```csharp
/// <summary>
/// Written as UTF-8 JSON to the stdin of the TFS inventory subprocess.
/// Credentials are passed here — never via command-line arguments.
/// </summary>
public sealed class TfsInventoryRequest
{
    /// <summary>TFS collection URL, e.g. http://tfs:8080/tfs/DefaultCollection</summary>
    public string CollectionUrl { get; set; } = string.Empty;

    /// <summary>PAT or Windows credential for authentication.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Optional project filter. Null or empty means "all projects".
    /// </summary>
    public string? Project { get; set; }

    /// <summary>API version string, e.g. "15.0".</summary>
    public string ApiVersion { get; set; } = "15.0";
}
```

### TFS stdout NDJSON line schema

Each NDJSON line from the TFS subprocess stdout is a JSON object with the following fields:

```json
{ "projectName": "MyProject", "workItemCount": 12345, "isComplete": true }
```

| Field | Type | Description |
|---|---|---|
| `projectName` | `string` | Team project name |
| `workItemCount` | `int` | Total work items found so far (may be partial if `isComplete=false`) |
| `isComplete` | `bool` | `true` when all pages for this project have been counted |

The .NET 10 host parses each line and converts the final (`isComplete=true`) entry per project into a `ProjectDiscoverySummary` for display and CSV output.

---

## Config Schema (v2.0)

The full `migration.json` schema for a config that uses inventory:

```json
{
  "configVersion": "2.0",
  "mode": "Export",
  "artefacts": { "path": "D:\\exports\\run-001" },
  "source": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/myorg",
    "project": "MyProject"
  },
  "target": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/target"
  },
  "modules": [],
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/myorg",
        "project": "MyProject",
        "token": "$ENV:ADO_PAT",
        "apiVersion": "7.1"
      }
    ]
  }
}
```

An inventory-only config (no migration sections required):
```json
{
  "configVersion": "2.0",
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/myorg",
        "token": "$ENV:ADO_PAT"
      }
    ]
  }
}
```

> **Note**: `mode`, `artefacts`, `source`, `target`, and `modules` remain optional for an inventory-only run. `MigrationOptionsValidator` must not reject a config that omits these fields when only the `inventory` section is present. This is the most significant validator change required by this feature.

---

## State Transitions

The inventory command's execution flow is a simple sequential state machine per source:

```
[Start]
  │
  ▼
[Resolve token]  ──fail──►  [Report error for source; continue to next]
  │
  ▼
[Discover projects]  ──fail──►  [Report error for source; continue to next]
  │ (zero projects)
  ├──► [Print "No projects found"; source succeeds]
  │
  ▼
[For each project: count work items (paginated)]
  │  per page: update live table
  │  ──api error mid-page──►  [Report partial count with warning; mark source failed]
  ▼
[Source complete: add to InventorySourceResult]
  │
  ▼
[All sources processed]
  │  (any source failed?)
  ├── yes ──► [Write CSV if --out; exit code 1]
  └── no  ──► [Write CSV if --out; exit code 0]
```

---

## Validation Rules

| Entity | Field | Rule | Error |
|---|---|---|---|
| `InventoryOptions` | `Sources` | Not null, at least 1 element | `"At least one source is required in inventory.sources."` |
| `InventorySourceOptions` | `Type` | Not null/empty; must be `AzureDevOpsServices` or `TeamFoundationServer` | `"inventory.sources[n].type is required and must be 'AzureDevOpsServices' or 'TeamFoundationServer'."` |
| `InventorySourceOptions` | `OrgOrCollection` | Not null/empty | `"inventory.sources[n].orgOrCollection is required."` |
| `InventorySourceOptions` | `Token` | Not null/empty | `"inventory.sources[n].token is required."` |
| `ITokenResolver` | raw token | `$ENV:` with empty variable name | `"Malformed token reference '$ENV:' — variable name is missing."` |
| `ITokenResolver` | env var | Variable not set | `"Environment variable '{name}' referenced in config token is not set."` |
| `MigrationOptionsValidator` | `ConfigVersion` | Must be `"1.0"` or `"2.0"` | `"ConfigVersion '{v}' is not supported. Supported: 1.0, 2.0."` |
| `MigrationOptionsValidator` | (inventory-only config) | `Mode`, `Source`, `Target`, `Artefacts` must not be required if no migration keys present | Validator updated: only require `Mode`/`Source`/`Target`/`Artefacts` when any migration key is non-empty |

---

## Relationships Diagram

```
migration.json (configVersion: "2.0")
    │
    ├── [existing migration sections: mode, source, target, artefacts, modules]
    │
    └── inventory
            └── sources[]
                    ├── InventorySourceOptions {type=AzureDevOpsServices}
                    │       │
                    │       ▼ ITokenResolver.Resolve(token)
                    │       │
                    │       ▼ ICatalogService.GetProjectsAsync()
                    │       │
                    │       └── ICatalogService.CountAllWorkItemsAsync()
                    │               └── yields ProjectDiscoverySummary (paginated)
                    │
                    └── InventorySourceOptions {type=TeamFoundationServer}
                            │
                            ▼ ITokenResolver.Resolve(token)
                            │
                            ▼ ExternalToolRunner.RunWithStdinAsync(
                            │     tfsmigration.exe, "inventory",
                            │     TfsInventoryRequest JSON
                            │   )
                            │       stdout → NDJSON lines
                            │       each line → ProjectDiscoverySummary
                            └── exit code 0 → success
```
