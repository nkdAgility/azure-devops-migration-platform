# Implementation Plan: Work Item ID Map — Integrity, Rebuild, and Sync Support (Reconciled)

**Branch**: `019-workitem-idmap-sync`  
**Date**: 2026-05-17  
**Spec**: `specs/019-workitem-idmap-sync/spec.md`

## Current Status

This artifact was previously a template placeholder. It has been replaced with a reconciliation snapshot based on current repository evidence.

- **Task status summary**: 16 complete, 23 incomplete, 3 complete/superseded
- **Primary completed capability areas**: target existence checks, seed/rebuild flow, integrity check invocation, feature/spec artifacts for package lock and idmap scenarios
- **Primary open capability areas**: idmap schema/contract alignment, package-lock integration into worker startup, control-plane status endpoint, documentation sync closure, final verification tasks

## Remaining Incomplete Work (Task IDs)

`T001, T002, T004, T006, T007, T008, T012, T013, T016, T019, T029, T030, T032, T033, T034, T035, T036, T037, T038, T039, T040, T041, T042`

## Completed/Superseded Work (Task IDs + Source)

- `T011` — superseded by `specs/035-workitem-import-support/tasks.md` (lock ownership moved to package access implementation; adapter retained)
- `T014` — superseded by `specs/035-workitem-import-support/tasks.md` (RevisionProcessResult promoted to shared abstraction)
- `T026` — superseded by `specs/035-workitem-import-support/tasks.md` (integrity-check loop encapsulated in `IIdMapStore.CheckIntegrityAsync`)

## Key Contradictions and Reconciliation Decisions

1. **IIdMapStore contract divergence**: spec expects new enumeration/skip signatures; implementation uses a different shape and central integrity API.
2. **Schema divergence**: spec expects `last_revision_index` column on `work_item_map`; implementation stores revision watermark in a dedicated table.
3. **Package lock architecture divergence**: spec expects direct lock service wiring in worker; implementation routes lock semantics through `IPackageAccess` and an adapter service.
4. **Control-plane endpoint gap**: model/adapter references exist, but `/agents/{agentInstanceId}/status` endpoint is not implemented in controller surface.

## Verification Evidence

- ✅ `dotnet build DevOpsMigrationPlatform.slnx` passed in this reconciliation session.
- ⚠️ `dotnet test DevOpsMigrationPlatform.slnx --no-build` was re-attempted in this reconciliation session and did not complete (stalled); no full-pass evidence captured yet.
- ⚠️ No debug-profile runtime verification evidence yet for `T039` lock lifecycle/integrity/rerun checks.

## Notes

This plan now reflects a reconciliation baseline, not a forward implementation design. Use `tasks.md` as the operational source of truth for per-task completion status and evidence notes.

