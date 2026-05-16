# Reconciliation Checklist: 008-simulated-data-source

**Purpose**: Align checklist truth with reconciled task status and repository evidence  
**Updated**: 2026-05-16  
**Feature**: [spec.md](../spec.md) | [tasks.md](../tasks.md)

## Task-status truth alignment

- [x] A reconciled `tasks.md` exists with one terminal status marker on every task line.
- [x] Complete tasks are checked `[x]`.
- [x] Incomplete tasks are unchecked `[ ]` and include Evidence notes.
- [x] Superseded tasks are checked `[x]` and include source + reason + evidence.

## Current implementation alignment

- [x] Simulated source/target are documented in canonical docs (`docs/capabilities-guide.md`, `docs/configuration-reference.md`).
- [x] Simulated export/import/roundtrip scenarios and launch profiles exist.
- [x] End-to-end simulated system tests exist (`SimulatedMigrationCommandTests`).
- [ ] Original flat-field seed/workItemCount contract is implemented exactly as specified.  
  Evidence: Current code and configs use generator-project/type model instead.
- [ ] Default 25k simulated scenario/profile exists and is validated in this spec scope.  
  Evidence: Current checked-in simulated scenarios are small datasets.

## Contradiction tracking

- [x] Stale discrepancy claims about missing Simulated docs were reconciled.
- [x] Command-surface mismatch (`discovery inventory` vs `queue` + `Mode: Inventory`) is documented in spec reconciliation.
- [x] Interface-signature drift (`CreateAsync(endpoint, ct)` vs `CreateAsync(ct)`) is documented in spec reconciliation.

## Notes

Checklist now reflects repository truth and reconciled task statuses, not the original draft readiness state.
