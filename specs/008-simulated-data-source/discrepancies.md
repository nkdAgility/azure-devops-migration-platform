# Architecture Discrepancies (Reconciled)

**Feature**: Simulated Data Source for End-to-End Migration Testing  
**Updated**: 2026-05-17

## Resolved discrepancies

### ✅ `source.type` / `target.type` missing `Simulated`
- **Resolution evidence**:
  - `docs/capabilities-guide.md` includes Simulated source and target sections.
  - `docs/configuration-reference.md` includes Simulated in `Source.Type` and `Target.Type`.

## Remaining discrepancies

### ⚠️ Spec contract vs implemented config model
- **Issue**: This spec's flat model (`source.seed`, `source.workItemCount`, `projectCount`, `avgRevisionsPerItem`, etc.) does not match current implemented generator model (`source.generator.projects[*].workItemTypes[*]...`).
- **Evidence**:
  - `scenarios/queue-export-workitems-simulated-source.json`
  - `src/DevOpsMigrationPlatform.Abstractions/Options/SimulatedEndpointOptions.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSource.cs`

### ⚠️ Spec command surface is stale
- **Issue**: Spec user stories reference `discovery inventory`, while current canonical behavior is `queue` with `Mode: Inventory`.
- **Evidence**:
  - `.agents/30-context/domains/cli-commands.md`
  - `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs` (inventory scenario uses `queue`)

### ⚠️ Spec performance/default scenario mismatch
- **Issue**: Spec requires a ready default 25k simulated scenario/profile, but checked-in scenarios are currently small datasets.
- **Evidence**:
  - `scenarios/queue-export-workitems-simulated-source.json`
  - `scenarios/roundtrip-simulated.json`
  - `.vscode/launch.json` simulated profiles

### ⚠️ Reconciliation artifact gap (`/speckit.analyze` blocker)
- **Issue**: `specs/008-simulated-data-source/plan.md` is missing, so `/speckit.analyze` cannot run to completion for this folder.
- **Evidence**:
  - `/speckit.analyze` output: missing required artifact `plan.md`; analysis aborted.
