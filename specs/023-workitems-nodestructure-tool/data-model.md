# Data Model — NodeStructure Tool

**Feature**: 023-workitems-nodestructure-tool  
**Date**: 2026-04-26

---

## Configuration Entities

### NodeStructureOptions

**Location**: `src/DevOpsMigrationPlatform.Abstractions/Options/NodeStructureOptions.cs`  
**Section**: `MigrationPlatform:Tools:NodeStructure`

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Master switch. When `false`, all import-side tool behaviour is bypassed. Export-side artifacts are still written. |
| `AreaPathMappings` | `IReadOnlyList<NodeMapping>` | `[]` | Ordered regex mapping rules for area paths. Each entry has `Match` (regex pattern) and `Replacement` (regex replacement). First match wins. Case-insensitive. |
| `IterationPathMappings` | `IReadOnlyList<NodeMapping>` | `[]` | Ordered regex mapping rules for iteration paths. Same semantics as `AreaPathMappings`. |
| `AreaLanguageOverride` | `string?` | `null` | When set, normalises the root segment of source area paths to this value. |
| `IterationLanguageOverride` | `string?` | `null` | When set, normalises the root segment of source iteration paths to this value. |
| `AutoCreateNodes` | `bool` | `false` | When `true`, creates missing area/iteration nodes in the target via ADO API. |
| `SkipOnUnresolvableArea` | `bool` | `false` | When `true`, skips revisions with unresolvable area paths. |
| `SkipOnUnresolvableIteration` | `bool` | `false` | When `true`, skips revisions with unresolvable iteration paths. |
| `ReplicateSourceTree` | `bool` | `false` | When `true`, import reads `Nodes/source-tree.json` and replicates all nodes to the target before processing revisions. |

**Constraints**:
- Class is `sealed` with `init`-only properties.
- Declares `public static string SectionName => "MigrationPlatform:Tools:NodeStructure";`
- `AreaPathMappings` and `IterationPathMappings` rules use `Regex.IsMatch` with `RegexOptions.IgnoreCase | RegexOptions.NonBacktracking`. Input paths are trimmed before matching.

---

## Domain Entities

### NodeMapping

**Location**: `src/DevOpsMigrationPlatform.Abstractions/Options/NodeMapping.cs`

| Property | Type | Description |
|---|---|---|
| `Match` | `string` | .NET regex pattern. Tested with `Regex.IsMatch(path, Match, RegexOptions.IgnoreCase \| RegexOptions.NonBacktracking)`. |
| `Replacement` | `string` | .NET regex replacement string. Supports `$1`, `$2`, etc. Applied via `Regex.Replace`. |

Record type. Immutable. Used in `NodeStructureOptions.AreaPathMappings` and `IterationPathMappings`.

### PathTranslation

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/PathTranslation.cs`

| Property | Type | Description |
|---|---|---|
| `TargetPath` | `string?` | The translated target path. `null` if unresolvable. |
| `MatchedByMap` | `bool` | `true` if the path matched an `AreaPathMappings`/`IterationPathMappings` regex rule. |
| `MatchedByProjectSwap` | `bool` | `true` if auto project-name swap was applied. |
| `IsExternalPath` | `bool` | `true` if the source path did not begin with the source project name. |

Record type. Immutable.

### ProjectMapping

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ProjectMapping.cs`

| Property | Type | Description |
|---|---|---|
| `SourceProjectName` | `string` | The source project name (from config/manifest). |
| `TargetProjectName` | `string` | The target project name (from config). |

Record type. Immutable.

### ClassificationNodeType

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ClassificationNodeType.cs`

Enum: `Area`, `Iteration`

### IterationNodeEntry

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IterationNodeEntry.cs`

Used for `Nodes/source-tree.json` streaming deserialization.

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | Full node path (e.g., `"ProjectName\\Area\\Child"`). |
| `StartDate` | `DateTimeOffset?` | Iteration start date (null for area nodes). |
| `FinishDate` | `DateTimeOffset?` | Iteration finish date (null for area nodes). |
| `IsBacklogIteration` | `bool` | `true` if this is the backlog iteration for the project. |

Record type. Immutable.

### ClassificationTreeSnapshot

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ClassificationTreeSnapshot.cs`

Represents the `Nodes/source-tree.json` package artifact.

| Property | Type | Description |
|---|---|---|
| `AreaNodes` | `IReadOnlyList<string>` | Area node paths (plain strings). |
| `IterationNodes` | `IReadOnlyList<IterationNodeEntry>` | Iteration nodes with optional dates and backlog flag. |

Record type. Used for serialization/deserialization only.

> **Note**: For streaming (FR-016), the import reads this artifact using `System.Text.Json` streaming APIs (`JsonSerializer.DeserializeAsyncEnumerable` or manual `Utf8JsonReader`) to avoid loading the full list into memory. The `ClassificationTreeSnapshot` type is used for export-time serialization and for ValidateAsync (where the full artifact is acceptable since it's a bounded set of nodes, not revisions).

### ReferencedPathsArtifact

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ReferencedPathsArtifact.cs`

Represents the `Nodes/referenced-paths.json` package artifact.

| Property | Type | Description |
|---|---|---|
| `AreaPaths` | `IReadOnlyList<string>` | Distinct area paths found in exported revisions. |
| `IterationPaths` | `IReadOnlyList<string>` | Distinct iteration paths found in exported revisions. |

Record type. Used for serialization/deserialization only.

### NodeReplicationProgress

**Location**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeReplicationProgress.cs`

Persisted in `IStateStore` under key `nodestructure-nodes-confirmed`.

| Property | Type | Description |
|---|---|---|
| `ReplicatedPaths` | `HashSet<string>` | Case-insensitive set of node paths confirmed present in the target. |
| `UpdatedAt` | `DateTimeOffset` | Last update timestamp (UTC). |

---

## Validation Entities

### NodeStructureValidationReport

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/NodeStructureValidationReport.cs`

| Property | Type | Description |
|---|---|---|
| `IsValid` | `bool` | `true` if no validation findings. |
| `UnmappedPaths` | `IReadOnlyList<UnmappedPathFinding>` | Paths that no mapping rule matched. |
| `UnanchoredPaths` | `IReadOnlyList<UnmappedPathFinding>` | Paths not anchored in the source project (external paths). |
| `MalformedTargetPaths` | `IReadOnlyList<string>` | Mapping rules whose `Replacement` produces a target path with empty value or ADO-illegal characters (`\`, `/`, `$`, `?`, `*`, `"`, `:`, `>`, `<`, `|`, `#`, `%`, `+`, control chars). |

### UnmappedPathFinding

| Property | Type | Description |
|---|---|---|
| `FieldName` | `string` | `"System.AreaPath"` or `"System.IterationPath"` |
| `Path` | `string` | The unmapped path value. |
| `AffectedRevisionCount` | `int` | Number of revisions containing this path. |

---

## Interface Contracts

### INodeStructureTool

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeStructureTool.cs`

```csharp
public interface INodeStructureTool
{
    /// <summary>
    /// Translates a single path value from source to target format.
    /// Pure transformation — no I/O.
    /// </summary>
    PathTranslation TranslatePath(
        string fieldName,
        string sourcePathValue,
        ProjectMapping context);

    /// <summary>Whether the tool is enabled.</summary>
    bool IsEnabled { get; }
}
```

### INodeCreator

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeCreator.cs`

```csharp
public interface INodeCreator
{
    /// <summary>Checks whether a classification node exists in the target.</summary>
    Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct);

    /// <summary>
    /// Ensures a node exists in the target. Creates it (and all ancestors) if missing.
    /// Idempotent: returns successfully if node already exists.
    /// </summary>
    Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct);

    /// <summary>
    /// Sets iteration start/finish dates on a target node.
    /// No-op if both dates are null.
    /// </summary>
    Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct);
}
```

### IClassificationTreeReader

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IClassificationTreeReader.cs`

```csharp
public interface IClassificationTreeReader
{
    /// <summary>
    /// Enumerates all area node paths from the source project.
    /// </summary>
    IAsyncEnumerable<string> EnumerateAreaNodesAsync(CancellationToken ct);

    /// <summary>
    /// Enumerates all iteration nodes (with dates and backlog flag) from the source project.
    /// </summary>
    IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(CancellationToken ct);
}
```

### INodeStructureValidator

**Location**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeStructureValidator.cs`

```csharp
public interface INodeStructureValidator
{
    /// <summary>
    /// Scans package for path coverage. Uses Nodes/referenced-paths.json if available,
    /// otherwise falls back to scanning all revision.json files.
    /// </summary>
    Task<NodeStructureValidationReport> ValidateAsync(
        IArtefactStore artefactStore,
        ProjectMapping context,
        CancellationToken ct);
}
```

---

## Relationships

```
NodeStructureOptions (config)
    ├── INodeStructureTool (pure path mapping)
    │     └── TranslatePath() called by:
    │           ├── RevisionFolderProcessor (WorkItemsModule import)
    │           ├── TeamsModule (future — team area/iteration settings)
    │           └── INodeStructureValidator (ValidateAsync pre-scan)
    │
    ├── INodeCreator (target I/O)
    │     └── EnsureExistsAsync() called by:
    │           ├── NodeEnsurer pre-collection step
    │           └── NodeEnsurer bulk replication from Nodes/source-tree.json
    │
    ├── IClassificationTreeReader (source I/O — export only)
    │     └── Called by ClassificationTreeCapture during export
    │
    └── INodeStructureValidator (package validation)
          └── Called by ValidateAsync pipeline
```

---

## State Transitions

### Path Translation Flow (per field value)

```
Source path value
  ↓
[Language override] → normalise root segment (if configured)
  ↓
[Regex matching] → iterate AreaPathMappings/IterationPathMappings rules (first match wins, Regex.IsMatch + Regex.Replace)
  ↓ (no match)
[Auto-swap check] → does path start with source project name?
  ├── Yes → substitute source prefix with target prefix
  └── No → pass through unchanged (mark as unanchored)
  ↓
NodeStructurePathResult
```

### Import Pre-Processing Flow

```
1. ReplicateSourceTree (if true)
   ├── Read Nodes/source-tree.json from package (streaming)
   ├── For each node: check checkpoint → skip if confirmed
   ├── EnsureExistsAsync() → create if missing
   ├── SetIterationDatesAsync() (iteration nodes with dates)
   └── Update checkpoint after each node

2. AutoCreateNodes pre-collection (if true)
   ├── Read Nodes/referenced-paths.json (fast path) or scan all revision folders
   ├── For each path: TranslatePath() → collect distinct translated paths
   └── EnsureExistsAsync() for each distinct path not in target

3. Revision processing loop (standard streaming import)
   └── RevisionFolderProcessor applies TranslatePath() per revision
```
