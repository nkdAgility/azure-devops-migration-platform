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
    string? Id,            // source-only metadata (ADO BoardRow.Id); NOT an import key
    string Name);          // portable key — ADO BoardRow exposes only Id + Name
```

> Mirrors `Microsoft.TeamFoundation.Work.WebApi.BoardRow`, which has exactly `Id` and `Name`
> — no colour or description. Earlier drafts had `Color`/`Description`; neither exists in the API.

**Validation rules**:
- `Name` must not be null or whitespace.
- `Id` is retained as source metadata only (FR-006) and is never used as an import key.

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
    string Name,             // display name e.g. "Stories" (BacklogLevelConfiguration.Name)
    string WitCategory,      // WIT category reference name e.g. "Microsoft.RequirementCategory"
    BacklogLevelType LevelType, // portfolio / requirement / task (BacklogLevelConfiguration.Type)
    int Rank);               // ordering within the backlog levels (BacklogLevelConfiguration.Rank; task backlog = 0)

// Domain mirror of Microsoft.TeamFoundation.Work.WebApi.BacklogType
public enum BacklogLevelType { Portfolio, Requirement, Task }
```

> Maps to `Microsoft.TeamFoundation.Work.WebApi.BacklogLevelConfiguration`, which exposes both
> `Type` (the backlog level type required by FR-004) and `Rank` (ordering). The earlier draft
> captured only `Rank` and omitted `Type`.

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

## Options (per-extension — the extension owns its own `IOptions<T>`)

Each extension owns its own, distinct options class. The board-config extension's config is **not**
nested inside a shared `TeamsModuleExtensionsOptions` god-object — it is bound independently via
`IOptions<BoardConfigExtensionOptions>`:

```csharp
/// <summary>Config for the board-config extension. Bound via IOptions<BoardConfigExtensionOptions>.</summary>
public sealed class BoardConfigExtensionOptions
{
    /// <summary>Optional extension → carries Enabled (a mandatory extension would not).</summary>
    public bool Enabled { get; init; } = true;

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

The extension returns `IsEnabled => _options.Enabled` from its **own** `IOptions<BoardConfigExtensionOptions>`.
Adding an extension never requires editing a central config class.

---

## Extension Architecture Contracts

### `IModuleExtension` — the single extension contract

Namespace: `DevOpsMigrationPlatform.Abstractions.Agent`

All extensions implement this **directly**. There is **no** `I{Domain}Extension` sub-interface.

```csharp
public interface IModuleExtension
{
    string Module { get; }       // owning module, e.g. "Teams"
    string Name { get; }         // unique within module, e.g. "BoardConfig"
    int Order { get; }           // lower runs first
    bool SupportsExport { get; }
    bool SupportsImport { get; }

    /// <summary>Parameterless — reads this extension's OWN IOptions<T>. Mandatory → true; optional → own Enabled.</summary>
    bool IsEnabled { get; }

    Task ExportAsync(IExtensionContext context, CancellationToken ct);
    Task ImportAsync(IExtensionContext context, CancellationToken ct);
}
```

**No default interface methods** — `Abstractions.Agent` targets `net481;net10.0` and net481 cannot
compile DIMs (CS8701). Every member is implemented on the class. Under "one type, both directions" an
extension normally implements both phases; a single-direction extension declares a one-line no-op
(`=> Task.CompletedTask`) for the unsupported phase and sets the matching `Supports*` flag `false`.
No abstract base class (single both-directions implementer today — YAGNI).

---

### `IExtensionContext` — module-neutral per-entity context

Namespace: `DevOpsMigrationPlatform.Abstractions.Agent`

```csharp
public interface IExtensionContext
{
    string Organisation { get; }
    string ProjectName { get; }
    string EntityId { get; }
    string? TargetEntityId { get; }   // null on export; set by orchestrator before import
    IPackageAccess Package { get; }
}
```

---

### `TeamExtensionContext` — Teams concrete context

Namespace: `DevOpsMigrationPlatform.Abstractions.Agent.Teams`

The host module supplies this concrete record; it does **not** carry a shared module options object —
each extension reads its own config. Extensions cast `IExtensionContext` to this type.

```csharp
public sealed record TeamExtensionContext : IExtensionContext
{
    public required string Organisation { get; init; }
    public required string ProjectName { get; init; }
    public required string EntityId { get; init; }      // source team id
    public string? TargetEntityId { get; init; }        // set by orchestrator on import
    public required IPackageAccess Package { get; init; }

    public required TeamDefinition Team { get; init; }
    public required string Slug { get; init; }
    public string? SourceProjectName { get; init; }     // for path translation on import
}
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

    // Granular board-config capabilities (spec FR-015 names these individually)
    BoardColumns  = 1 << 0,   // Kanban board columns
    BoardRows     = 1 << 1,   // Kanban board swimlanes (rows)
    CardRules     = 1 << 2,   // Card styling rules

    // Other board-related capabilities
    Backlogs      = 1 << 3,   // Backlog metadata from /backlogs endpoint
    TaskboardColumns = 1 << 4, // Sprint taskboard columns

    // Composite — the Kanban board configuration as a whole.
    // BoardConfig consists of BoardColumns + BoardRows + CardRules
    // (extend this composite if further board-config sub-capabilities are added).
    BoardConfig   = BoardColumns | BoardRows | CardRules,
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

| Connector | BoardConfig (Columns+Rows+CardRules) | TaskboardColumns | Backlogs | Registration |
|-----------|-------------|-----------|----------|-------------|
| AzureDevOpsServices | ✅ | ✅ | ✅ | `StaticConnectorCapabilityProvider(BoardConfig \| TaskboardColumns \| Backlogs)` |
| Simulated | ✅ | ✅ | ✅ | `StaticConnectorCapabilityProvider(BoardConfig \| TaskboardColumns \| Backlogs)` |
| TeamFoundationServer | ❌ | ❌ | ❌ | `TfsConnectorCapabilityProvider` → `ConnectorCapability.None` (explicit, testable) |

> `BoardConfig` is a composite flag (`BoardColumns | BoardRows | CardRules`). A connector that registers `BoardConfig` therefore satisfies `Has(BoardColumns)`, `Has(BoardRows)`, and `Has(CardRules)`. Per-feature checks (e.g. swimlanes → `BoardRows`) let a future connector support a subset; today TFS supports none and ADO/Simulated support all.

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
