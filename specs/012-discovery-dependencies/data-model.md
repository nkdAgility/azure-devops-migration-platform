# Data Model: Discovery Dependency Analysis

**Feature**: `012-discovery-dependencies`  
**Phase**: 1 — Design

---

## New Types

### `LinkScope` (enum) — `Abstractions`

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Models;

public enum LinkScope
{
    CrossProject,        // Target work item is in a different project within the same organisation/collection
    CrossOrganisation    // Target work item is in a different organisation or TFS collection
    // SameProject is intentionally absent — those links are never represented
}
```

**Constraints**: `SameProject` is not a valid enum value. Links that resolve to the same project are discarded before any `DependencyRecord` is created.

---

### `TargetStatus` (enum) — `Abstractions`

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Models;

public enum TargetStatus
{
    Reachable,      // Target work item was accessible (HTTP 200)
    Deleted,        // Target work item returned HTTP 404
    AccessDenied,   // Target work item returned HTTP 401 / 403
    Unknown         // Target unreachable for any other reason (network error, cross-org unauthenticated, etc.)
}
```

---

### `DependencyRecord` (record) — `Abstractions`

Single external outbound link from a source work item to a target in a different project or organisation. This is the unit written as one CSV row.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Models;

public record DependencyRecord
{
    public int    SourceWorkItemId   { get; init; }
    public string SourceWorkItemType { get; init; } = string.Empty;
    public string SourceProject      { get; init; } = string.Empty;
    public string LinkType           { get; init; } = string.Empty;   // Human-readable, e.g. "Child", "Related"
    public LinkScope LinkScope       { get; init; }
    public int    TargetWorkItemId   { get; init; }
    public string TargetProject      { get; init; } = string.Empty;   // Empty when CrossOrganisation and project is unknown
    public string TargetOrganisation { get; init; } = string.Empty;   // Host/URL of the target org
    public TargetStatus TargetStatus { get; init; }
}
```

**CSV column mapping (FR-006)**:

| Property | CSV Column Name |
|----------|----------------|
| `SourceWorkItemId` | `SourceWorkItemId` |
| `SourceWorkItemType` | `SourceWorkItemType` |
| `SourceProject` | `SourceProject` |
| `LinkType` | `LinkType` |
| `LinkScope` | `LinkScope` |
| `TargetWorkItemId` | `TargetWorkItemId` |
| `TargetProject` | `TargetProject` |
| `TargetOrganisation` | `TargetOrganisation` |
| `TargetStatus` | `TargetStatus` |

---

### `DependencySummary` (record) — `Abstractions`

Aggregated counts used for the terminal summary table (FR-007).

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Models;

public record DependencySummary
{
    public int WorkItemsAnalysed   { get; init; }
    public int ExternalLinksFound  { get; init; }
    public int CrossProjectCount   { get; init; }
    public int CrossOrgCount       { get; init; }
    public string ReportFilePath   { get; init; } = string.Empty;
}
```

---

### `DependencyProgressEvent` (abstract record) — `Abstractions`

Discriminated union emitted by `IDependencyDiscoveryService.DiscoverDependenciesAsync`. The `DependencyCommand` pattern-matches on derived types.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Models;

public abstract record DependencyProgressEvent;

/// <summary>A single external link record ready for CSV output.</summary>
public sealed record DependencyFoundEvent(DependencyRecord Record) : DependencyProgressEvent;

/// <summary>Periodic progress update (no record to write).</summary>
public sealed record DependencyHeartbeatEvent(
    string  OrganisationUrl,
    string  ProjectName,
    int     WorkItemsAnalysed,
    int     ExternalLinksFound,
    int     CrossProjectCount,
    int     CrossOrgCount,
    bool    IsComplete,
    string? Error = null) : DependencyProgressEvent;
```

---

### `IDependencyDiscoveryService` (interface) — `Abstractions/Services`

Platform-agnostic orchestrator for dependency discovery across all configured organisations.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Services;

public interface IDependencyDiscoveryService
{
    IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
        string?           wiqlFilter,
        CancellationToken cancellationToken = default);
}
```

---

### `IWorkItemLinkAnalysisService` (interface) — `Abstractions/Services`

Per-organisation link analysis. Implemented by `AzureDevOpsDependencyAnalysisService`, `SimulatedDependencyAnalysisService`, and delegated by `TfsDependencyProcessAdapter`.

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Services;

public interface IWorkItemLinkAnalysisService
{
    IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
        string            organisationUrl,
        string            project,
        string            pat,
        string?           wiqlFilter,
        CancellationToken cancellationToken = default);
}
```

---

### `DependencyCommandSettings` (nested class) — `CLI.Migration`

Spectre.Console settings for `DependencyCommand`. Registered under `discovery dependencies`.

```csharp
public sealed class Settings : BaseCommandSettings
{
    [CommandOption("--output <PATH>")]
    [Description("File path for the output CSV (default: discovery-dependencies.csv in CWD)")]
    public string? OutputPath { get; set; }

    [CommandOption("--wiql <EXPRESSION>")]
    [Description("WIQL filter to scope which work items are analysed. Omit to analyse all work items.")]
    public string? WiqlFilter { get; set; }
}
```

---

## Existing Types Modified

### `DiscoveryOptions` — `Abstractions`

Add `MaxConcurrency` to control bounded parallel API calls:

```csharp
// New property added to DevOpsMigrationPlatform.Abstractions.Options.DiscoveryOptions:
/// <summary>Maximum concurrent API batch calls during dependency analysis. Default: 4.</summary>
public int MaxConcurrency { get; set; } = 4;
```

---

## Service Implementations

### `DependencyDiscoveryService` — `Infrastructure`

Orchestrates across all enabled `DiscoveryOptions.Organisations`. For each org entry:
- `Type == "AzureDevOpsServices"` → delegates to `IWorkItemLinkAnalysisService` (ADO implementation)
- `Type == "Simulated"` → delegates to `SimulatedDependencyAnalysisService`
- `Type == "TeamFoundationServer"` → delegates to `TfsDependencyProcessAdapter`

Yields `DependencyProgressEvent` records from each org in sequence (not interleaved — maintains predictable CLI output ordering).

### `AzureDevOpsDependencyAnalysisService` — `Infrastructure.AzureDevOps`

Implements `IWorkItemLinkAnalysisService` for ADO Services:

1. Execute WIQL query (default: all work items in project) using `WorkItemTrackingHttpClient.QueryByWiqlAsync`.
2. Batch-GET work items with `$expand=relations` using `GetWorkItemsBatchAsync` (batches of 200).
3. For each relation: parse `url` to extract the host and work item ID.
4. Classify: if host ≠ source org host → skip (CrossOrganisation candidate → secondary lookup for TargetProject is skipped; TargetProject = ""; TargetOrganisation = parsed host).
5. For same-host relations: collect target IDs and batch-GET their `System.TeamProject`. If project matches source → discard (SameProject). If different → `CrossProject` with resolved TargetProject.
6. Set `TargetStatus` based on HTTP response codes (see research.md #1).
7. Emit `DependencyFoundEvent` for each `CrossProject` or `CrossOrganisation` record.
8. Emit `DependencyHeartbeatEvent` after every batch of 200 source work items processed.
9. Respects `DiscoveryOptions.MaxConcurrency` via `SemaphoreSlim`.

### `SimulatedDependencyAnalysisService` — `Infrastructure`

Generates deterministic synthetic `DependencyFoundEvent` records using a seeded `Random`. Number of records is derived from the `Simulated` org entry's `workItemCount` and a configurable `avgLinksPerItem` (default: 3). 70% CrossProject, 30% CrossOrganisation.

### `TfsDependencyProcessAdapter` — `CLI.Migration`

Spawns `tfsmigration.exe dependencies` via `IExternalToolRunner`. Passes credentials via stdin JSON. Reads NDJSON stdout lines and maps each to `DependencyFoundEvent` or `DependencyHeartbeatEvent`.

**Subprocess stdin request format**:
```json
{
  "collectionUrl": "http://tfs.internal:8080/tfs/DefaultCollection",
  "project": "MyProject",
  "pat": "",               // empty = Windows-integrated auth
  "wiqlFilter": null       // null = all work items
}
```

**Subprocess NDJSON output format** (same base format as other adapters):
```json
{ "type": "dependency-found", "record": { "sourceWorkItemId": 42, ... } }
{ "type": "heartbeat", "workItemsAnalysed": 200, "externalLinksFound": 15, ... }
```

---

## Type Locations Summary

| Type | Project | Path |
|------|---------|------|
| `LinkScope` | `Abstractions` | `Models/LinkScope.cs` |
| `TargetStatus` | `Abstractions` | `Models/TargetStatus.cs` |
| `DependencyRecord` | `Abstractions` | `Models/DependencyRecord.cs` |
| `DependencySummary` | `Abstractions` | `Models/DependencySummary.cs` |
| `DependencyProgressEvent` | `Abstractions` | `Models/DependencyProgressEvent.cs` |
| `IDependencyDiscoveryService` | `Abstractions` | `Services/IDependencyDiscoveryService.cs` |
| `IWorkItemLinkAnalysisService` | `Abstractions` | `Services/IWorkItemLinkAnalysisService.cs` |
| `DependencyDiscoveryService` | `Infrastructure` | `Services/DependencyDiscoveryService.cs` |
| `SimulatedDependencyAnalysisService` | `Infrastructure` | `Services/SimulatedDependencyAnalysisService.cs` |
| `AzureDevOpsDependencyAnalysisService` | `Infrastructure.AzureDevOps` | `Services/AzureDevOpsDependencyAnalysisService.cs` |
| `DependencyServiceCollectionExtensions` | `Infrastructure.AzureDevOps` | `DependencyServiceCollectionExtensions.cs` |
| `TfsDependencyProcessAdapter` | `CLI.Migration` | `TfsDependencyProcessAdapter.cs` |
| `DependencyCommand` | `CLI.Migration` | `Commands/Discovery/DependencyCommand.cs` |
