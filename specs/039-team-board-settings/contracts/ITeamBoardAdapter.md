# Contract: ITeamBoardAdapter

**Layer**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Teams`  
**File**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/ITeamBoardAdapter.cs`

---

## Purpose

Adapter contract for per-team board configuration. Covers both directions:

- **Export** — reads board config from a live system into the package
- **Import** — writes board config from the package into a live system

A single adapter implementation handles both. This mirrors the module and
extension pattern: `IModule` carries both `ExportAsync` and `ImportAsync`;
`IModuleExtension` carries both; so does the adapter.

---

## Interface

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Adapter contract for per-team board configuration.
/// Covers read (export) and write (import) in one interface.
/// </summary>
public interface ITeamBoardAdapter
{
    // -------------------------------------------------------------------------
    // Export — read from live system
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enumerates all Kanban boards for the team, including columns and swimlanes.
    /// Returns one <see cref="BoardConfig"/> per board (one per backlog level).
    /// Returns an empty sequence for a team with no boards — must not throw.
    /// </summary>
    IAsyncEnumerable<BoardConfig> GetBoardsAsync(
        string project,
        string teamId,
        CancellationToken ct);

    /// <summary>
    /// Returns the card rule settings (colour-coding) for a specific board.
    /// Returns <see langword="null"/> if the board has no card rules configured.
    /// </summary>
    Task<CardRuleSettings?> GetCardRuleSettingsAsync(
        string project,
        string teamId,
        string boardName,
        CancellationToken ct);

    /// <summary>
    /// Enumerates backlog levels, returning display name and WIT category for each.
    /// Does NOT include visibility flags (those are in TeamSettings).
    /// </summary>
    IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project,
        string teamId,
        CancellationToken ct);

    /// <summary>
    /// Enumerates sprint taskboard columns for the team.
    /// </summary>
    IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project,
        string teamId,
        CancellationToken ct);

    // -------------------------------------------------------------------------
    // Import — write to live system
    // -------------------------------------------------------------------------

    /// <summary>
    /// Replaces or merges the Kanban column configuration for a specific board.
    /// </summary>
    Task UpdateBoardColumnsAsync(
        string project,
        string teamId,
        string boardName,
        IReadOnlyList<BoardColumn> columns,
        CancellationToken ct);

    /// <summary>
    /// Replaces or merges the swimlane (row) configuration for a specific board.
    /// </summary>
    Task UpdateSwimLanesAsync(
        string project,
        string teamId,
        string boardName,
        IReadOnlyList<BoardSwimLane> swimLanes,
        CancellationToken ct);

    /// <summary>
    /// Replaces the card rule settings for a specific board.
    /// Passing <see langword="null"/> clears all card rules on the target board.
    /// </summary>
    Task UpdateCardRuleSettingsAsync(
        string project,
        string teamId,
        string boardName,
        CardRuleSettings? rules,
        CancellationToken ct);

    /// <summary>
    /// Replaces or merges the sprint taskboard column configuration for the team.
    /// </summary>
    Task UpdateTaskboardColumnsAsync(
        string project,
        string teamId,
        IReadOnlyList<TaskboardColumn> columns,
        CancellationToken ct);

    /// <summary>
    /// Reads current Kanban columns from the live system. Used by Merge mode to
    /// compute a delta before writing.
    /// </summary>
    Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(
        string project,
        string teamId,
        string boardName,
        CancellationToken ct);

    /// <summary>
    /// Reads current swimlanes from the live system. Used by Merge mode.
    /// </summary>
    Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(
        string project,
        string teamId,
        string boardName,
        CancellationToken ct);

    /// <summary>
    /// Reads current taskboard columns from the live system. Used by Merge mode.
    /// </summary>
    Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(
        string project,
        string teamId,
        CancellationToken ct);
}
```

---

## Implementors

| Connector | Class | Notes |
|-----------|-------|-------|
| AzureDevOpsServices | `AzureDevOpsBoardAdapter` | Uses `WorkHttpClient` with `TeamContext` |
| Simulated | `SimulatedBoardAdapter` | Deterministic canned data for export; captures write calls in-memory for test assertion |
| TeamFoundationServer | *(not registered)* | `ConnectorCapability.BoardConfig` not set; `BoardConfigTeamExtension` returns `Skipped` before reaching the adapter |

---

## Error Behaviour

- `Get*` methods **must not** throw for a team with no boards — return an empty sequence or empty list.
- `Update*` methods: if `boardName` does not exist on the target, **throw** a domain exception. The extension catches this, logs a structured warning, and continues to the next board.
- `UpdateCardRuleSettingsAsync(null)` is a valid call — it clears card rules on the target.
- All methods **must** propagate `OperationCanceledException`.

---

## Pre-conditions (Import)

- Team identity must exist in the target project before any write call (FR-017).
- The team must have at least one Kanban board enabled on the target process template.

---

## Related

- [`BoardConfigTeamExtension.md`](BoardConfigTeamExtension.md) — the extension that consumes this adapter (implements `IModuleExtension`)
- [`IModuleExtension.md`](IModuleExtension.md) — the single extension contract
- [`data-model.md`](../data-model.md) — full record definitions
