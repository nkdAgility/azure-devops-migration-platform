# Research: Team Board Configuration Export/Import

**Phase**: 0 — Outline & Research  
**Created**: 2026-06-09  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md)

---

## 1. ConnectorCapability Runtime Flag Mechanism

### Decision
Introduce a `ConnectorCapability` enum (or flags enum) that connectors register at startup.
Each board config extension checks the registered capabilities at runtime and returns
`TaskExecutionResult.Skipped(...)` if the required capability is absent — rather than using
`#if NET481` guards in the orchestrator.

### Rationale
- `#if NET481` guards are already used for TFS import dead-ends at the module level (TeamsModule).
- For board config specifically, TFS may support *some* of the board APIs in future versions.
- A runtime capability flag is more granular, observable, and extensible.
- Keeps TFS connector code unchanged — it simply does not register `ConnectorCapability.BoardConfig`.

### Pattern
```csharp
// In the connector registration (AzureDevOpsServices):
services.AddSingleton<IConnectorCapabilityProvider>(
    _ => new StaticConnectorCapabilityProvider(
        ConnectorCapability.BoardConfig |
        ConnectorCapability.Taskboard));

// In the export orchestrator:
if (!_capabilityProvider.Has(ConnectorCapability.BoardConfig))
{
    _logger.LogInformation("[Teams] Connector does not support BoardConfig — skipping.");
    return TaskExecutionResult.Skipped("Connector does not declare BoardConfig capability.");
}
```

### Alternatives Considered
- **`#if NET481` in orchestrator**: rejected — couples TFS dead-end to compile target rather than connector declaration.
- **Null source guard only** (`if (_boardSource is null)`): acceptable fallback, but less expressive — doesn't distinguish "not registered" from "registered but unavailable".
- **Separate module for board config**: rejected — adds DI complexity with no user benefit; board config is inherently per-team.

---

## 2. Azure DevOps Board Config APIs

### Decision
Use the following Azure DevOps REST API endpoints (via the .NET client library
`Microsoft.TeamFoundationServer.Client`):

| Extension | API Endpoint | Client Method |
|-----------|-------------|---------------|
| Kanban Columns | `GET /_apis/work/boards/{board}/columns` | `WorkHttpClient.GetBoardColumnsAsync` |
| Swimlanes | `GET /_apis/work/boards/{board}/rows` | `WorkHttpClient.GetBoardRowsAsync` |
| Card Rules | `GET /_apis/work/boards/{board}/cardrulesettings` | `WorkHttpClient.GetBoardCardRuleSettingsAsync` |
| Backlogs Metadata | `GET /_apis/work/backlogs` | `WorkHttpClient.GetBacklogsAsync` |
| Taskboard Columns | `GET /_apis/work/taskboardColumns` | `WorkHttpClient.GetTaskboardColumnsAsync` |
| Board list (per team) | `GET /_apis/work/boards` | `WorkHttpClient.GetBoardsAsync` |

### Rationale
- The `WorkHttpClient` is already used by the AzureDevOpsServices connector for other team data.
- All endpoints require `TeamContext` (project + team name/id) — available from `TeamDefinition`.
- Boards are identified by name (e.g. "Stories", "Bugs") — names are stable keys for the package.

### TFS Availability
The `WorkHttpClient` REST client is available on TFS 2017+ but the board columns/rows/card rules
endpoints are not reliably implemented. The TFS connector does NOT register `ITeamBoardAdapter`,
and the `ConnectorCapability.BoardConfig` flag is not set, so all board extensions skip at runtime.

### Key Gotchas
- A project may have multiple boards per team (one per backlog level with "Kanban" enabled).
  Export iterates all boards returned by `GetBoardsAsync` for the team context.
- Board names may contain characters invalid for filesystem paths (`/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|`).
  The package key (artefact address) must sanitise board names the same way `TeamSlugGenerator`
  sanitises team names. See Section 5 (Path Sanitisation) below.
- Card rule settings may reference custom field reference names — these are stored verbatim
  in the package and are validated at import (RT-H1 deferral: validation is a warning, not hard error).

---

## 3. Import Modes (Replace / Merge / Skip)

### Decision
Single `importMode` property at the `BoardConfigExtensionsOptions` level, shared by all five
board config extensions:

```csharp
public enum BoardConfigImportMode
{
    Replace, // Default: overwrite target board config with package values
    Merge,   // Overlay: only set columns/rows/rules that differ; preserve extras
    Skip     // No-op: leave target board config unchanged
}
```

### Rationale
- A single flag is simpler for operators than per-extension flags.
- `Replace` (default) gives deterministic results on first run.
- `Merge` supports incremental patching when the target has been partially configured manually.
- `Skip` allows operators to run a full package import while intentionally deferring board config.

### Merge Semantics (per extension)
| Extension | Merge behaviour |
|-----------|----------------|
| Columns | Upsert by column name; preserve target-only columns at end |
| Swimlanes | Upsert by name; preserve target-only swimlanes at end |
| Card rules | Overwrite (card rules are a single blob, no meaningful merge unit) |
| Backlogs | Upsert by WIT category; skip unknown categories |
| Taskboard columns | Upsert by column name; preserve target-only columns |

---

## 4. Package File Layout

### Decision
Board config is stored **separately** from `team.json` to:
- Allow partial re-export without overwriting team identity data.
- Enable the `BoardConfigExtensions.Enabled` flag to independently gate the file.

```text
Teams/{slug}/
├── team.json               # existing — identity, settings, iterations, members, area paths
└── board-config.json       # NEW — all board config for this team
```

### board-config.json Shape
```json
{
  "exportedAt": "2026-06-09T...",
  "teamName": "My Team",
  "boards": [
    {
      "boardName": "Stories",
      "columns": [...],
      "swimlanes": [...]
    }
  ],
  "cardRules": { ... },
  "backlogs": [...],
  "taskboardColumns": [...]
}
```

### Rationale
Separating the file keeps `team.json` stable for operators who are familiar with its structure,
and allows `AlwaysExport`-style skipping at the `board-config.json` level independently.

---

## 5. Path Sanitisation for Board Names

### Decision
Reuse the same slugification logic as `TeamSlugGenerator` for board names used as package artefact
address components. Board names are normalised (lowercase, filesystem-safe characters only) before
being used in `PackageContentContext`.

### Rationale
Board names like "Stories" are safe, but some organisations use names with special characters.
Failing to sanitise causes `IArtefactStore` path errors at runtime. The existing `TeamSlugGenerator`
pattern is the canonical approach in this codebase.

### Implementation Note
Board name sanitisation is needed only for path construction inside the orchestrator.
The `boardName` field in `board-config.json` stores the **original unsanitised** name for
display and as the import key. The package address uses the sanitised form.
Since all board config for a team is written to a single `board-config.json`, board names
appear only as keys within the JSON, not as filesystem path segments — path sanitisation risk
is therefore **low**. Confirm during implementation.

---

## 6. Simulated Connector Behaviour

### Decision
The Simulated connector provides deterministic in-memory board config data for testing.
It registers `ConnectorCapability.BoardConfig` and returns canned data (2 boards, 3 columns each,
1 swimlane, empty card rules, 2 backlogs, 3 taskboard columns).

### Rationale
Full connector coverage (Constitution Principle XI) requires Simulated to support board config.
Deterministic canned data supports repeatable ATDD scenario execution.

---

## 7. Checkpoint / Resume for Board Config Export

### Decision
Board config export runs inside the existing per-team checkpoint loop in `TeamsOrchestrator`.
No additional checkpoint level is added. If a run fails mid-export, the team's checkpoint is
not advanced, so the full team (including board config) is re-exported on resume.

### Rationale
Board config export is fast (5–6 API calls per team). Re-exporting it on resume has negligible
cost compared to the complexity of a nested checkpoint cursor.

### Caveat (RT-M3 deferral)
If future profiling shows board config export is slow (e.g. large card rule sets), a nested
cursor checkpoint can be added without changing the package format. Deferred.

---

## All NEEDS CLARIFICATION Resolved

| # | Clarification | Resolution |
|---|---------------|------------|
| C1 | TFS dead-end pattern | `ConnectorCapability` flag; TFS does not register `ITeamBoardAdapter` |
| C2 | Import strategy | `importMode` at group level: Replace / Merge / Skip |
| C3 | Backlogs duplication | Backlogs extension captures display name + WIT category only; visibility in existing TeamSettings |
| C4 | ConnectorCapability mechanism | `IConnectorCapabilityProvider` + `ConnectorCapability` flags enum; registered per connector |
| C5 | Board name path safety | Sanitisation only needed for package address; board names stored verbatim in JSON |
| C6 | Checkpoint granularity | Reuse existing per-team checkpoint; board config is fast enough |
