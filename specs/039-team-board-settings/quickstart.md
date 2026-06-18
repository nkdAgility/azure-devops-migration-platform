# Quickstart Validation Guide: Team Board Configuration Export/Import

**Phase**: 1 — Design & Contracts  
**Created**: 2026-06-09  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md) | [data-model.md](data-model.md)

This guide describes how to verify the feature works end-to-end once implemented.

---

## Prerequisites

- .NET 10 SDK installed
- Repository cloned and solution built (`dotnet build`)
- Test runner: `dotnet test` (MSTest + Reqnroll)

---

## Scenario 1 — Export board config (Simulated connector)

**What it proves**: Board columns, swimlanes, card rules, backlogs, and taskboard columns
are exported to `Teams/{slug}/board-config.json` for each team.

**Run**:
```bash
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests \
  --filter "Category=BoardConfig&Category=Export"
```

**Expected outcome**:
- Each team in the simulated package has a `Teams/{slug}/board-config.json` artefact.
- The JSON contains `boards` (≥1 entry), `backlogs` (≥1 entry), `taskboardColumns` (≥1 entry).
- `cardRules` is either a valid object or `null`.

**Key test** (from `BoardConfigTeamExtensionTests.cs`):
```csharp
// [TestMethod] ExportAsync_SimulatedConnector_WritesExpectedBoardConfig
// Given a Simulated source with one team "Alpha" having 2 boards
// When the TeamsModule exports to a package
// Then "Teams/alpha/board-config.json" exists in the package
// And the board-config contains 2 boards with at least 2 columns each
```

---

## Scenario 2 — TFS connector returns Skipped

**What it proves**: When the source connector does not declare `ConnectorCapability.BoardConfig`,
the export extension skips without error.

**Run**:
```bash
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests \
  --filter "Category=BoardConfig&Category=TFS"
```

**Expected outcome**:
- No `board-config.json` artefacts written.
- Module result is `Skipped` for board config extensions.
- No exception thrown; log contains "Connector does not declare BoardConfig capability."

---

## Scenario 3 — Import with Replace mode (Simulated connector)

**What it proves**: Board columns are written to the target connector in Replace mode.

**Run**:
```bash
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests \
  --filter "Category=BoardConfig&Category=Import&Category=Replace"
```

**Expected outcome**:
- `SimulatedBoardAdapter.UpdateBoardColumnsAsync` called once per board per team.
- Column names on the target match the package values.

---

## Scenario 4 — Import with Skip mode

**What it proves**: `importMode: Skip` leaves the target board unchanged.

**Run**:
```bash
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests \
  --filter "Category=BoardConfig&Category=Import&Category=Skip"
```

**Expected outcome**:
- No `UpdateBoard*` calls made on `SimulatedBoardAdapter`.
- Module result is `Skipped` for board config import.

---

## Scenario 5 — Extensions disabled in options

**What it proves**: Setting `BoardConfig.Columns = false` skips column export/import only.

**Run**:
```bash
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests \
  --filter "Category=BoardConfig&Category=ExtensionDisabled"
```

**Expected outcome**:
- `board-config.json` is written but `boards[].columns` is `null` or absent.
- Other extensions (swimlanes, card rules) are still present if enabled.

---

## Manual Validation (against real AzureDevOpsServices)

> Requires a PAT and a test project with at least one team and one Kanban board.

```bash
# Export
dotnet run --project src/DevOpsMigrationPlatform.Agent \
  -- export \
  --source https://dev.azure.com/my-org/my-project \
  --package ./test-package \
  --modules Teams

# Check artefact
ls ./test-package/Teams/*/board-config.json

# Import to target
dotnet run --project src/DevOpsMigrationPlatform.Agent \
  -- import \
  --target https://dev.azure.com/my-org/my-target-project \
  --package ./test-package \
  --modules Teams
```

**Expected**: Board columns on the target project match those in `board-config.json`.

---

## References

- [Data Model](data-model.md) — record shapes and validation rules
- [ITeamBoardAdapter contract](contracts/ITeamBoardAdapter.md)
- [Spec FR-015, FR-016, FR-018](spec.md) — capability detection, import mode, ConnectorCapability
