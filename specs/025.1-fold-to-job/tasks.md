# Reconciliation Tasks — 025.1-fold-to-job

## Current status

- Reconciled against repository state on 2026-05-17.
- This file is the canonical task-status ledger for spec 025.1.

## Remaining incomplete work

- T007
- T008
- T014

## Completed because superseded

- T005
- T012
- T013

## Contradictions and reconciliation

- FR-007 strict guard behavior in this spec conflicts with current compatible-resume overwrite behavior.
- Scenario 1 dispatch expectation conflicts with current `Inventory` routing.
- Legacy `.jsonl` log filename claims conflict with current `.ndjson` contract.

## Verification evidence

- `src\DevOpsMigrationPlatform.Abstractions\Jobs\Job.cs`
- `src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs`
- `src\DevOpsMigrationPlatform.TfsMigrationAgent\TfsJobAgentWorker.cs`
- `src\DevOpsMigrationPlatform.Abstractions\Streaming\ProgressEvent.cs`
- `src\DevOpsMigrationPlatform.Infrastructure.Agent\Context\JobExecutionPlanBuilder.cs`
- `src\DevOpsMigrationPlatform.Infrastructure.Agent\Context\JobPlanExecutor.cs`
- `src\DevOpsMigrationPlatform.ControlPlane\Controllers\TelemetryController.cs`
- `src\DevOpsMigrationPlatform.ControlPlane\Controllers\ProgressController.cs`
- `src\DevOpsMigrationPlatform.CLI.Migration\Commands\QueueCommand.cs`
- `tests\DevOpsMigrationPlatform.CLI.Migration.Tests\TestUtilities\ControlPlaneHostRunner.cs`

## Canonical task lines

- [X] T001 [US1] Consolidate to single `Job` dispatch type — src\DevOpsMigrationPlatform.Abstractions\Jobs\Job.cs — Status: complete
- [X] T002 [US1] Dispatch by `Job.Kind` with supported kinds — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: complete
- [X] T003 [US1] Use `Job.Connectors` for routing capabilities — src\DevOpsMigrationPlatform.Abstractions\Jobs\Job.cs — Status: complete
- [X] T004 [US1] Carry full config as `Job.ConfigPayload` from CLI — src\DevOpsMigrationPlatform.CLI.Migration\Commands\QueueCommand.cs — Status: complete
- [X] T005 [US1] Materialize payload before module config consumption — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption/tasks.md T027
- [X] T006 [US1] Build per-job configuration/options scope before execution — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: complete
- [ ] T007 [US1] Enforce strict re-submission fail when config exists without ForceFresh — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: incomplete
- [ ] T008 [US1] Flush all sinks before terminal signal on every terminal path — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: incomplete
- [X] T009 [US2] Isolate system-test storage via `DEVOPS_MIGRATION_TEST_STORAGE` — tests\DevOpsMigrationPlatform.CLI.Migration.Tests\TestUtilities\CliRunner.cs — Status: complete
- [X] T010 [US2] Use `%DEVOPS_MIGRATION_TEST_STORAGE%` in scenarios — scenarios\*.json — Status: complete
- [X] T011 [US2] Serialize ControlPlaneHost startup for parallel tests — tests\DevOpsMigrationPlatform.CLI.Migration.Tests\TestUtilities\ControlPlaneHostRunner.cs — Status: complete
- [X] T012 [US3] Persist terminal logs as `progress.jsonl` and `agent.jsonl` — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories/tasks.md T084
- [X] T013 [US1] Keep original inventory discovery dispatch split — src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs — Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task/spec.md FR-001
- [ ] T014 [US3] Keep DoD evidence counts current and reproducible — specs\025.1-fold-to-job\spec.md — Status: incomplete

## Evidence notes — incomplete

- T007: `WriteConfigPayloadAsync` allows overwrite when source/target identity is compatible; strict-fail behavior is not enforced.
- T008: explicit pre-signal flush exists in normal terminal paths, but some fail-fast signal paths do not perform explicit local pre-signal flush.
- T014: retrospective DoD count claims in `spec.md` are stale relative to current repository tests/scenarios.

## Evidence notes — superseded

- T005: package metadata routing moved config materialization expectations under `IPackageAccess` package-manager adoption.
- T012: runtime log stream contract uses `.migration/runs/<runId>/logs/progress.ndjson` and `diagnostics.ndjson`.
- T013: execution semantics now derive from job task plans and `TaskKind` orchestration, replacing old migration/discovery split expectations.
