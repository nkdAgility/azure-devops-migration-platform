# Data Model: Team Board Configuration Export/Import

**Phase**: 1 — Design & Contracts  
**Created**: 2026-06-09  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md) | [research.md](research.md)

---

## Package Artefact

All board config for a team is stored in a single file:

```text
Teams/{slug}/board-config.json
```

This is separate from the existing `Teams/{slug}/team.json`.

---

## Records (Abstractions Layer)

All records are in namespace `DevOpsMigrationPlatform.Abstractions.Agent.Teams`.

### `TeamBoardConfig` — top-level package model

```csharp
public sealed record TeamBoardConfig(
    string TeamName,
    DateTimeOffset ExportedAt,
    IReadOnlyList<BoardConfig> Boards,
    CardRuleSettings? CardRules,        // null if connector does not support card rules
    IReadOnlyList<BacklogMetadata> Backlogs,
    IReadOnlyList<TaskboardColumn> TaskboardColumns);
```

**Validation rules**:
- `TeamName` must not be null or whitespace.
- `Boards` must not be null (may be empty list).
- `Backlogs` must not be null (may be empty list).
- `TaskboardColumns` must not be null (may be empty list).

---

### `BoardConfig` — one Kanban board (one per backlog level)

```csharp
public sealed record BoardConfig(
    string BoardName,
    IReadOnlyList<BoardColumn> Columns,
    IReadOnlyList<BoardSwimLane> SwimLanes);
```

**Validation rules**:
- `BoardName` must not be null or whitespace.
- `Columns` must have at least 2 entries (Incoming + Outgoing).

---

### `BoardColumn` — a Kanban column

```csharp
public sealed record BoardColumn(
    string Name,
    string ColumnType,           // "incoming" | "inProgress" | "outgoing"
    int ItemLimit,               // 0 = no limit
    bool IsSplit,
    string? Description,
    IReadOnlyList<BoardColumnStateMapping> StateMappings);
```

**Validation rules**:
- `Name` must not be null or whitespace.
- `ColumnType` must be one of the three known values.
- `ItemLimit` must be >= 0.

---

### `BoardColumnStateMapping` — maps a WIT category to a workflow state

```csharp
public sealed record BoardColumnStateMapping(
    string WorkItemType,
    string State);
```

---

### `BoardSwimLane` — a horizontal row (swimlane)

```csharp
public sealed record BoardSwimLane(
    string Name,
    string? Color);
```

**Validation rules**:
- `Name` must not be null or whitespace.

---

### `CardRuleSettings` — colour-coding rules for cards

```csharp
public sealed record CardRuleSettings(
    IReadOnlyList<CardRule> Rules);

public sealed record CardRule(
    string Name,
    string? Color,
    bool IsEnabled,
    string Filter);    // raw filter expression e.g. "[Priority] = 1"
```

**Validation rules**:
- `Filter` is stored verbatim; field-reference validity is checked at import time (warning, not error).

---

### `BacklogMetadata` — display name and WIT category for a backlog level

```csharp
public sealed record BacklogMetadata(
    string Name,           // display name e.g. "Stories"
    string WitCategory,    // WIT category reference name e.g. "Microsoft.RequirementCategory"
    int Rank);             // ordering within the backlog levels
```

**Note**: Backlog visibility flags are **not** stored here; they are part of the existing
`TeamSettings` export (work settings). See FR-004 in spec.

---

### `TaskboardColumn` — a sprint taskboard column

```csharp
public sealed record TaskboardColumn(
    string Name,
    string ColumnType,     // "inProgress" | "done" etc.
    int Order,
    IReadOnlyList<BoardColumnStateMapping> StateMappings);
```

---

## Options (Extensions to TeamsModuleExtensionsOptions)

```csharp
/// <summary>Controls board configuration export/import extensions.</summary>
public sealed class BoardConfigExtensionsOptions
{
    /// <summary>Export/import Kanban board columns.</summary>
    public bool Columns { get; init; } = true;

    /// <summary>Export/import board swimlanes (rows).</summary>
    public bool SwimLanes { get; init; } = true;

    /// <summary>Export/import card rule settings (colour-coding).</summary>
    public bool CardRules { get; init; } = true;

    /// <summary>Export backlog display name and WIT category metadata.</summary>
    public bool Backlogs { get; init; } = true;

    /// <summary>Export/import sprint taskboard columns.</summary>
    public bool TaskboardColumns { get; init; } = true;

    /// <summary>
    /// Import strategy applied uniformly to all board config types.
    /// Replace (default): overwrite target with package values.
    /// Merge: overlay package values; preserve target-only entries.
    /// Skip: leave target unchanged.
    /// </summary>
    public BoardConfigImportMode ImportMode { get; init; } = BoardConfigImportMode.Replace;
}

public enum BoardConfigImportMode { Replace, Merge, Skip }
```

`TeamsModuleExtensionsOptions` gains a new property:

```csharp
/// <summary>Controls which board configuration extensions are active.</summary>
public BoardConfigExtensionsOptions BoardConfig { get; init; } = new();
```

---

## Extension Architecture Contracts

### `IModuleExtension` — cross-cutting marker

Namespace: `DevOpsMigrationPlatform.Abstractions.Agent`

```csharp
/// <summary>Cross-cutting marker for all per-entity module extensions.</summary>
public interface IModuleExtension
{
    /// <summary>Name of the module this extension belongs to (e.g. "Teams").</summary>
    string Module { get; }

    /// <summary>Name of this extension (e.g. "BoardConfig", "TeamSettings").</summary>
    string Name { get; }

    /// <summary>Declared execution order within the module. Lower = earlier.</summary>
    int Order { get; }

    bool SupportsExport { get; }
    bool SupportsImport { get; }
}
```

---

### `ITeamExtension` — Teams per-entity extension contract

Namespace: `DevOpsMigrationPlatform.Abstractions.Agent.Teams`

```csharp
/// <summary>
/// Extension that participates in per-team export and/or import.
/// Export and import are capabilities on a single extension — not separate types.
/// </summary>
public interface ITeamExtension : IModuleExtension
{
    string IModuleExtension.Module => "Teams";

    /// <summary>Returns true when this extension is active under the current options.</summary>
    bool IsEnabled(TeamsModuleExtensionsOptions options);

    /// <summary>Executes the export phase for one team.</summary>
    Task ExportAsync(TeamExtensionContext context, CancellationToken ct);

    /// <summary>Executes the import phase for one team.</summary>
    Task ImportAsync(TeamExtensionContext context, CancellationToken ct);
}
```

**Default implementations** (via interface default methods where `SupportsExport`/`SupportsImport` is false):
- `ExportAsync` returns `Task.CompletedTask` when `SupportsExport == false`
- `ImportAsync` returns `Task.CompletedTask` when `SupportsImport == false`

---

### `TeamExtensionContext` — shared per-team context

Namespace: `DevOpsMigrationPlatform.Abstractions.Agent.Teams`

```csharp
/// <summary>
/// Passed to every ITeamExtension per team. Export and import share the same record.
/// Extensions read from Package (import) or write to Package (export) via IPackageAccess.
/// </summary>
public sealed record TeamExtensionContext(
    string Organisation,
    string Project,
    string SourceProject,
    TeamDefinition Team,
    string Slug,
    IPackageAccess Package,
    TeamsModuleExtensionsOptions Extensions,
    IProgressSink? ProgressSink);
```

---

## Interfaces (Abstractions Layer)

See [contracts/](contracts/) for full interface contracts.

### `ITeamBoardAdapter`

Single adapter contract — covers both export (read) and import (write).
Mirrors the module and extension pattern: one type, both directions.

```csharp
public interface ITeamBoardAdapter
{
    // Export — read from live system
    IAsyncEnumerable<BoardConfig> GetBoardsAsync(string project, string teamId, CancellationToken ct);
    Task<CardRuleSettings?> GetCardRuleSettingsAsync(string project, string teamId, string boardName, CancellationToken ct);
    IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(string project, string teamId, CancellationToken ct);
    IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(string project, string teamId, CancellationToken ct);

    // Import — write to live system
    Task UpdateBoardColumnsAsync(string project, string teamId, string boardName, IReadOnlyList<BoardColumn> columns, CancellationToken ct);
    Task UpdateSwimLanesAsync(string project, string teamId, string boardName, IReadOnlyList<BoardSwimLane> swimlanes, CancellationToken ct);
    Task UpdateCardRuleSettingsAsync(string project, string teamId, string boardName, CardRuleSettings? rules, CancellationToken ct);
    Task UpdateTaskboardColumnsAsync(string project, string teamId, IReadOnlyList<TaskboardColumn> columns, CancellationToken ct);

    // Merge-mode reads — current state of the live system
    Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(string project, string teamId, string boardName, CancellationToken ct);
    Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(string project, string teamId, string boardName, CancellationToken ct);
    Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(string project, string teamId, CancellationToken ct);
}
```

| Connector | Implementation |
|-----------|---------------|
| AzureDevOpsServices | `AzureDevOpsBoardAdapter` |
| Simulated | `SimulatedBoardAdapter` |
| TeamFoundationServer | *(not registered — `ConnectorCapability.None`)* |

---

## ConnectorCapability

```csharp
[Flags]
public enum ConnectorCapability
{
    None          = 0,
    BoardConfig   = 1 << 0,   // Kanban columns, swimlanes, card rules
    Taskboard     = 1 << 1,   // Sprint taskboard columns
    Backlogs      = 1 << 2,   // Backlog metadata from /backlogs endpoint
}

public interface IConnectorCapabilityProvider
{
    bool Has(ConnectorCapability capability);
}
```

All three connectors **explicitly register** `IConnectorCapabilityProvider` in their DI setup.
TFS registers with `ConnectorCapability.None` — it does NOT omit registration.
This satisfies runtime-compatibility Rule 7: "DI registration must not be used to hide
capability gaps." No null-guard (`if (_provider is null)`) appears in any orchestrator.

| Connector | BoardConfig | Taskboard | Backlogs | Registration |
|-----------|-------------|-----------|----------|-------------|
| AzureDevOpsServices | ✅ | ✅ | ✅ | `StaticConnectorCapabilityProvider(BoardConfig \| Taskboard \| Backlogs)` |
| Simulated | ✅ | ✅ | ✅ | `StaticConnectorCapabilityProvider(BoardConfig \| Taskboard \| Backlogs)` |
| TeamFoundationServer | ❌ | ❌ | ❌ | `TfsConnectorCapabilityProvider` → `ConnectorCapability.None` (explicit, testable) |

---

## State Transitions

```
Export:
  team enumeration → [board config enabled?]
      yes → GetBoardsAsync → GetCardRuleSettingsAsync → GetBacklogsAsync → GetTaskboardColumnsAsync
          → serialize TeamBoardConfig → persist board-config.json
      no  → skip (no artefact written)

Import:
  read board-config.json → [ConnectorCapability.BoardConfig?]
      no  → Skipped
      yes → [importMode?]
          Skip   → Skipped
          Replace → UpdateBoardColumnsAsync, UpdateSwimLanesAsync, UpdateCardRuleSettingsAsync, UpdateTaskboardColumnsAsync
          Merge   → fetch target state → diff → apply delta only
```
