# Reconciliation Checklist: TUI Job Dashboard

**Purpose**: Align checklist truth with reconciled task status and repository evidence  
**Reconciled**: 2026-05-17  
**Feature**: [spec.md](../spec.md)

## Status Alignment

- [X] `tasks.md` now has one final status marker per task line.
- [X] Checkbox semantics are aligned (`complete`/`complete/superseded` = `[X]`, `incomplete` = `[ ]`).
- [X] Superseded items cite a concrete supersession source under `specs/.../tasks.md`.
- [X] Incomplete items include a short evidence note.

## Current Delivery Truth

- [X] Core TUI/runtime foundations are implemented (T001–T024, T026, T029, T036, T038–T041, T044).
- [ ] Required TUI-focused tests remain missing (`T025`, `T030`, `T031`, `T033`, `T034`, `T035`, `T037`, `T043`).
- [ ] `--job` existence/visibility verification remains incomplete (`T032`).
- [ ] Full `dotnet test` completion evidence for this reconciliation run is not available (`T042` failed after `dotnet clean` because test assemblies were missing for `--no-build`).

## Supersession Truth

- [X] Path and abstraction moves from separation-of-concerns are reflected (`T003`, `T009`).
- [X] Command-surface changes to `queue/prepare` are reflected (`T017`, `T018`, `T019`, `T020`).
- [X] Task/bootstrapped TUI evolution is reflected (`T028`).

## Release-gate snapshot (`/speckit.checklist`)

- [ ] Release-gate checklist passes (current result: 8 PASS / 12 FAIL).
- [ ] FR-012 contradiction (prepare exclusion vs actual prepare submission) resolved in canonical wording.
- [ ] TUI mode contract contradiction (Progress/Diagnostics vs Trace/Logs/Metrics-Feed) resolved or re-scoped.

## Notes

- This checklist now represents reconciliation status, not pre-planning readiness.
- Remaining work is tracked by incomplete IDs in `tasks.md`, `spec.md`, and `plan.md`.
