# Tasks: Job Execution Plan Bootstrap (Reconciled)

## Current status

- Reconciled against repository implementation and newer specs on 2026-05-17.
- Canonical status source for this spec is this file.

## Task list

- [X] T001 [US1] Create `JobTaskStatus` enum — `src\DevOpsMigrationPlatform.Abstractions\ControlPlaneApi\JobTaskStatus.cs` — Status: complete
- [X] T002 [US1] Create `JobTask` record — `src\DevOpsMigrationPlatform.Abstractions\ControlPlaneApi\JobTask.cs` — Status: complete
- [X] T003 [US1] Create `JobTaskList` record — `src\DevOpsMigrationPlatform.Abstractions\ControlPlaneApi\JobTaskList.cs` — Status: complete
- [X] T004 [US2] Add `TaskId`, `TaskStatus`, `CompletedCount` to `ProgressEvent` — `src\DevOpsMigrationPlatform.Abstractions\Streaming\ProgressEvent.cs` — Status: complete
- [X] T005 [US3] Add `Tasks` to bootstrap response — `src\DevOpsMigrationPlatform.Abstractions\ControlPlaneApi\JobBootstrap.cs` — Status: complete
- [X] T006 [US1] Create job execution plan feature file — `features\platform\job-execution-plan.feature` — Status: complete
- [X] T007 [US2] Create task attribution feature file — `features\platform\task-attribution.feature` — Status: complete
- [X] T008 [US1] Create `IJobExecutionPlanBuilder` — `src\DevOpsMigrationPlatform.Abstractions.Agent\Context\IJobExecutionPlanBuilder.cs` — Status: complete
- [X] T009 [US1] Create `JobExecutionPlanBuilder` implementation — `src\DevOpsMigrationPlatform.Infrastructure.Agent\Context\JobExecutionPlanBuilder.cs` — Status: complete
- [X] T010 [US1] Register plan builder in core services — `src\DevOpsMigrationPlatform.Infrastructure.Agent\CoreAgentServiceExtensions.cs` — Status: complete
- [ ] T011 [US1] Create `IJobTaskStore` interface — `src\DevOpsMigrationPlatform.ControlPlane\Store\IJobTaskStore.cs` — Status: incomplete
- [X] T012 [US1] Create in-memory task store — `src\DevOpsMigrationPlatform.ControlPlane\Jobs\InMemoryJobTaskStore.cs` — Status: complete
- [X] T013 [US1] Register task store in DI — `src\DevOpsMigrationPlatform.ControlPlane\Jobs\ControlPlaneServiceExtensions.cs` — Status: complete
- [ ] T014 [US1] Add POST tasks endpoint with empty-list validation — `src\DevOpsMigrationPlatform.ControlPlane\Controllers\TelemetryController.cs` — Status: incomplete
- [X] T015 [US3] Include tasks in bootstrap endpoint — `src\DevOpsMigrationPlatform.ControlPlane\Controllers\TelemetryController.cs` — Status: complete
- [X] T016 [US3] Add GET tasks endpoint — `src\DevOpsMigrationPlatform.ControlPlane\Controllers\TelemetryController.cs` — Status: complete
- [ ] T017 [US2] Derive task states from progress with unknown-task warning logging — `src\DevOpsMigrationPlatform.ControlPlane\Controllers\ProgressController.cs` — Status: incomplete
- [ ] T018 [US2] Add dedicated controller task-state tests — `tests\DevOpsMigrationPlatform.ControlPlane.Tests\Controllers\ProgressController_TaskStateTests.cs` — Status: incomplete
- [X] T019 [US2] Add `PushTaskListAsync` to telemetry client interface — `src\DevOpsMigrationPlatform.Abstractions\ControlPlaneApi\IControlPlaneTelemetryClient.cs` — Status: complete
- [X] T020 [US2] Implement `PushTaskListAsync` in telemetry client — `src\DevOpsMigrationPlatform.Infrastructure.Agent\Telemetry\ControlPlaneTelemetryClient.cs` — Status: complete
- [X] T021 [US2] Update `JobAgentWorker` to build/push plan and emit task lifecycle status — `src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs` — Status: complete
- [ ] T022 [US2] Update `TfsJobAgentWorker` to emit `TaskId`/`TaskStatus` in NDJSON lifecycle events — `src\DevOpsMigrationPlatform.TfsMigrationAgent\TfsJobAgentWorker.cs` — Status: incomplete
- [ ] T023 [US1] Add Reqnroll step definitions for job execution plan feature — `tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\Platform\JobExecutionPlanSteps.cs` — Status: incomplete
- [ ] T024 [US2] Add Reqnroll step definitions for task attribution feature — `tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\Platform\TaskAttributionSteps.cs` — Status: incomplete
- [X] T025 [US1] Add unit tests for plan builder behavior — `tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\Context\JobExecutionPlanBuilderTests.cs` — Status: complete
- [X] T026 [US2] Add unit tests for in-memory task store behavior — `tests\DevOpsMigrationPlatform.ControlPlane.Tests\Services\InMemoryJobTaskStoreTests.cs` — Status: complete
- [ ] T027 [US1] Build verification (`dotnet clean && dotnet build`) — repository-wide — Status: incomplete
- [ ] T028 [US1] Test verification (`dotnet test`) — repository-wide — Status: incomplete
- [ ] T029 [US1] Manual queue/bootstrap verification run — `.vscode\launch.json` profile — Status: incomplete

## Remaining incomplete work

T011, T014, T017, T018, T022, T023, T024, T027, T028, T029

## Completed because superseded

None.

## Incomplete evidence notes

- T011: No `IJobTaskStore` interface exists under `src\DevOpsMigrationPlatform.ControlPlane\Store`; current implementation uses concrete `InMemoryJobTaskStore` in `...ControlPlane\Jobs`.
- T014: `POST /agents/lease/{leaseId}/tasks` exists, but no `taskList.Tasks.Count > 0` validation/400 behavior is implemented.
- T017: Unknown task-id warning behavior is not logged; updates no-op silently in `InMemoryJobTaskStore.UpdateTask`.
- T018: Planned file `ProgressController_TaskStateTests.cs` does not exist; only broader progress/store tests exist.
- T022: `TfsJobAgentWorker` updates persisted plan status but does not emit `ProgressEvent.TaskId`/`TaskStatus` NDJSON lifecycle fields.
- T023/T024: No step definition files found at planned paths.
- T027/T028/T029: No fresh verification evidence recorded in this spec folder.

## Superseded evidence notes

None.
