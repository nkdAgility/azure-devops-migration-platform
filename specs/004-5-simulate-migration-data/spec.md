# Reconciliation Specification: 004-5 Simulate Migration Data

## Current status

- This folder had no `spec.md`; it referenced `specs/008-simulated-data-source/spec.md` as its upstream requirements source.
- Reconciliation completed against current repository implementation and newer related specs (`017`, `021.1`, `035`).
- `tasks.md` is now fully status-marked per task with evidence-backed `complete`, `incomplete`, or `complete/superseded`.

## Remaining incomplete work (IDs)

- `T034` — no automated 25k performance-gate test currently verifies the threshold.
- `T039` — quickstart walkthrough is stale and has not been validated end-to-end against current scenario/test paths.

## Completed because superseded (IDs + source)

- Superseded by `specs/017-simulated-infrastructure/spec.md` and `specs/021.1-simulated-infrastructure/spec.md` architecture changes: `T003`, `T004`, `T005`, `T006`, `T007`, `T009`, `T010`, `T012`, `T014`, `T016`, `T017`, `T018`, `T021`, `T022`, `T023`, `T024`, `T025`, `T026`, `T027`, `T028`, `T029`, `T030`, `T031`, `T032`, `T033`, `T038`.
- Import-surface supersession is additionally aligned with `specs/035-workitem-import-support/spec.md` (`IWorkItemImportTarget` path and broader import model).

## Contradictions and reconciliation

- Original plan/tasks assumed `IWorkItemImportSink`; current code uses `IWorkItemImportTarget`/`IWorkItemImportTargetFactory`.
- Original plan/tasks used stale paths (`Infrastructure/.../WorkItemsModule.cs`, `specs/copilot/...`, `migrate-simulated-25k.json`, `DevOpsMigrationPlatform.SystemTests`).
- Newer specs standardized polymorphic endpoint options and keyed connector registration; this replaced several original task intents without leaving placeholders.

## Verification evidence

- Implementation evidence inspected in:
  - `src/DevOpsMigrationPlatform.Infrastructure.Simulated/*`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Import/*`
  - `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs`
  - `.vscode/launch.json`, `scenarios/*simulated*.json`, `docs/*`
- Validation runs:
  - `dotnet build DevOpsMigrationPlatform.slnx -v minimal` (pass)
  - `dotnet test tests\DevOpsMigrationPlatform.Infrastructure.Simulated.Tests\DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.csproj -v minimal` (43 passed)
  - attempted targeted CLI simulated tests; run hung in this environment and was stopped.
