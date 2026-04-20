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
    [Description("File path for the work-item dependency CSV (default: discovery-dependencies.csv in CWD)")]
    public string? OutputPath { get; set; }

    [CommandOption("--output-projects <PATH>")]
    [Description("File path for the project dependency summary CSV (default: discovery-project-dependencies.csv alongside --output)")]
    public string? OutputProjectsPath { get; set; }

    [CommandOption("--output-diagram <PATH>")]
    [Description("File path for the Mermaid diagram (default: discovery-project-dependencies.md alongside --output)")]
    public string? OutputDiagramPath { get; set; }

    [CommandOption("--wiql <EXPRESSION>")]
    [Description("WIQL filter to scope which work items are analysed. Omit to analyse all work items.")]
    public string? WiqlFilter { get; set; }
}
```

---

### `ProjectPairKey` (record struct) — `CLI.Migration`

Lightweight key for the streaming project-pair accumulator dictionary. A record struct avoids heap allocation per key.

```csharp
namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// Immutable key for one directed project-to-project dependency pair.
/// Used as the dictionary key in the streaming aggregation accumulator.
/// <see cref="TargetOrganisation"/> is empty for CrossProject pairs.
/// </summary>
internal readonly record struct ProjectPairKey(
    string SourceProject,
    string TargetProject,       // Remote org hostname for CrossOrganisation pairs
    string TargetOrganisation,  // Empty for CrossProject
    LinkScope LinkScope);
```

**Memory note**: At P = 1,000 distinct projects, the dictionary holds at most P² = 1,000,000 keys × ~80–100 bytes = ~100 MB worst-case. Typical orgs with ≤100 projects: ~1 MB. The key count is bounded by project count squared, not by link count.

---

### `ProjectDependencyRecord` (record) — `CLI.Migration`

The aggregated row written to `discovery-project-dependencies.csv` (FR-015). Computed after the streaming pass from the accumulator.

```csharp
namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// One aggregated directed project-to-project dependency pair for the project summary CSV.
/// GroupId is assigned by Union-Find over project nodes after the streaming pass completes.
/// </summary>
internal record ProjectDependencyRecord
{
    public string    SourceProject      { get; init; } = string.Empty;
    public string    TargetProject      { get; init; } = string.Empty; // Hostname for cross-org
    public string    TargetOrganisation { get; init; } = string.Empty; // Empty for cross-project
    public int       LinkCount          { get; init; }
    public LinkScope LinkScope          { get; init; }
    public int       GroupId            { get; init; }                 // Connected component label, 1-based
}
```

**CSV column mapping (FR-015)**:

| Property | CSV Column Name |
|----------|----------------|
| `SourceProject` | `SourceProject` |
| `TargetProject` | `TargetProject` |
| `TargetOrganisation` | `TargetOrganisation` |
| `LinkCount` | `LinkCount` |
| `LinkScope` | `LinkScope` |
| `GroupId` | `GroupId` |

---

### `MermaidDiagramBuilder` (class) — `CLI.Migration`

Builds a `flowchart LR` Mermaid diagram from the completed project-pair accumulator. Called once after the streaming pass. Writes directly to a `StreamWriter`.

```csharp
namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// Generates a Mermaid flowchart LR diagram from aggregated project-pair data.
/// Cross-org boundary nodes receive the :::external CSS class.
/// All node IDs are sanitised (non-alphanumeric → underscore; prefixed P_).
/// </summary>
internal sealed class MermaidDiagramBuilder
{
    // Node ID: P_ProjectName (sanitised)
    // Node label: P_ProjectName["Project Name"]  (original name in quotes)
    // Cross-org node: P_hostname["hostname"]:::external
    // Edge: SourceId -->|"42 links"| TargetId
    // Footer: classDef external fill:#f96,stroke:#c63,color:#000

    public void Write(
        IEnumerable<ProjectDependencyRecord> pairs,
        StreamWriter writer);

    private static string SanitiseNodeId(string raw);  // Replace [^a-zA-Z0-9] with _, prefix P_
}
```

**Sanitisation rule**: `Regex.Replace(raw, @"[^a-zA-Z0-9]", "_")`, then prepend `P_`. Label always uses original name double-quoted inside square brackets. Ensures compatibility with Mermaid v10 and GitHub/ADO wiki rendering (SC-007).

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
| `ProjectPairKey` | `CLI.Migration` | `Commands/Discovery/ProjectPairKey.cs` |
| `ProjectDependencyRecord` | `CLI.Migration` | `Commands/Discovery/ProjectDependencyRecord.cs` |
| `MermaidDiagramBuilder` | `CLI.Migration` | `Commands/Discovery/MermaidDiagramBuilder.cs` |
