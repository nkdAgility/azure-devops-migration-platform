# Implementation Plan: Job Execution Plan Bootstrap

**Branch**: `028.1-task-bootstrap` | **Date**: 2026-05-01 | **Spec**: [spec.md](spec.md)

## Summary

Introduce `JobTask`, `JobTaskStatus`, `JobTaskList` in `Abstractions`. Add `TaskId?` + `TaskStatus?` to `ProgressEvent` (nullable — no breaking change). Add `Tasks?` to `JobBootstrap`. Build `IJobExecutionPlanBuilder` in `Abstractions.Agent` + `Infrastructure.Agent`. Add `PushTaskListAsync` to `IControlPlaneTelemetryClient`. Add `POST /agents/lease/{leaseId}/tasks` to `TelemetryController`. Update `ProgressController` to drive task state from ProgressEvent `TaskId`/`TaskStatus`. Update `JobAgentWorker` and `TfsJobAgentWorker` to build and push the plan and emit `TaskId` on every module lifecycle event.

## Current status (reconciled)

- Reconciled on 2026-05-17.
- This plan has partial drift versus implementation due follow-on specs (`028.2`, `030`, `032`, `033`, `034`).
- Canonical completion state is tracked in `tasks.md` in this folder.

## Remaining incomplete work

- T011, T014, T017, T018, T022, T023, T024, T027, T028, T029.

## Completed because superseded

- None recorded in this plan reconciliation.

## Contradictions and reconciliation

- Execution now includes plan persistence and broader task taxonomy (`capture`, `analyse`) beyond the initial bootstrap-only intent.
- `IJobExecutionPlanBuilder` shape and call flow differ from initial draft (`BuildAndSaveAsync` and package-boundary usage are present).
- Control-plane task storage uses `InMemoryJobTaskStore` directly instead of planned `IJobTaskStore` interface.

## Verification evidence

- Verified runtime and API wiring in:
  - `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs`
  - `src/DevOpsMigrationPlatform.ControlPlane/Controllers/ProgressController.cs`
  - `src/DevOpsMigrationPlatform.ControlPlane/Jobs/InMemoryJobTaskStore.cs`
  - `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
  - `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs`

## Technical Context

**Language/Version**: C# 12, .NET 10 (CLI + agent + control plane); .NET 4.8 (TFS agent — `Abstractions.Agent` is multi-targeted)
**Primary Dependencies**: `Microsoft.Extensions.Configuration`, `IPhaseTrackingService` (resume), `IArtefactStore` (inventory check), existing `IControlPlaneTelemetryClient`
**Testing**: MSTest + Reqnroll; `[TestCategory("SystemTest_Simulated")]` for end-to-end
**No breaking changes**: `ProgressEvent` gains nullable fields; `JobBootstrap` gains a nullable property — existing serialisation of both types is unaffected

## Constitution Check

- [x] **Package-First**: No new package I/O. Plan builder reads the existing `inventory.json` artefact path; writes nothing.
- [x] **Streaming**: Not applicable — no new module processing.
- [x] **Module Isolation**: Improved — `IJobExecutionPlanBuilder` is a focused new service; modules are unaware of it.
- [x] **Separation of Planes**: Control plane stores task state; agent pushes it; CLI/TUI reads it. No migration logic in CLI.
- [x] **Determinism**: Plan is derived from config + resume state — deterministic for the same inputs.
- [x] **ATDD-First**: Feature files written (Phase 2) before implementation phases.
- [x] **SOLID & DI**: `IJobExecutionPlanBuilder` interface in `Abstractions.Agent`; implementation in `Infrastructure.Agent`; registered via `AddCoreAgentServices`.

## Observability Contract

| Operation | Span (O-1) | Metrics (O-2) | Logs (O-3) | Progress (O-4) |
|-----------|-----------|--------------|-----------|----------------|
| `job.plan.push` | Tag `job.task_count` on existing startup span | None | `Information`: "Task list pushed — {TaskCount} tasks for job {JobId}" | N/A |
| `job.task.transition` | None | None | `Debug`: "Task {TaskId} transitioned {OldStatus} → {NewStatus}"; `Warning`: "Unknown TaskId {TaskId} in ProgressEvent for job {JobId}" | N/A |

---

## Phase 1: New Shared Types (Blocking Prerequisite)

All phases depend on these types being available to compile against.

- [ ] T001 Create `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTaskStatus.cs`
  — `public enum JobTaskStatus { Pending, Running, Completed, Failed, Skipped }`
- [ ] T002 Create `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTask.cs`
  — sealed record with `init`-only properties: `Id` (string), `Name` (string), `Phase` (string?), `Order` (int), `Status` (JobTaskStatus), `KnownTotal` (long?), `CompletedCount` (long?), `StartedAt` (DateTimeOffset?), `CompletedAt` (DateTimeOffset?), `SkipReason` (string?)
- [ ] T003 Create `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTaskList.cs`
  — sealed record: `Tasks: IReadOnlyList<JobTask>`, `PushedAt: DateTimeOffset`
- [ ] T004 Update `src/DevOpsMigrationPlatform.Abstractions/Streaming/ProgressEvent.cs`
  — add `TaskId: string?` and `TaskStatus: JobTaskStatus?` (both `init`-only, nullable, default null); add `CompletedCount: long?` for use on completion events
- [ ] T005 Update `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobBootstrap.cs`
  — add `Tasks: JobTaskList?` (`init`-only, nullable)

**Checkpoint**: `dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — 0 errors; no existing tests broken

---

## Phase 2: Feature Files (Parallel with Phase 3)

- [ ] T006 [P] Create `features/platform/job-execution-plan.feature`
  — tag `@platform`; scenarios for US-1 and US-3: bootstrap includes task list; all tasks Pending before execution; Skipped tasks on resume; Inventory skipped when inventory artefact exists
- [ ] T007 [P] Create `features/platform/task-attribution.feature`
  — tag `@platform`; scenarios for US-2: ProgressEvent carries TaskId on module start/complete/fail; task transitions reflected in bootstrap; unknown TaskId tolerated without error

---

## Phase 3: Agent — IJobExecutionPlanBuilder (Parallel with Phase 2)

- [ ] T008 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobExecutionPlanBuilder.cs`
  — interface: `Task<JobTaskList> BuildPlanAsync(IConfiguration packageConfig, JobKind kind, CancellationToken ct)`
  — multi-targeted (`net481;net10.0`) — no C# 11+ features
- [ ] T009 Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
  — sealed implementation of `IJobExecutionPlanBuilder`:
  - Reads enabled modules from `packageConfig` keys: `MigrationPlatform:Modules:{Name}:Enabled` for Identities, Nodes, Teams, WorkItems
  - Task list by `JobKind`:
    - `Export` → `[Inventory(order:0), Identities/Export(1), Nodes/Export(2), Teams/Export(3), WorkItems/Export(4)]` filtered to enabled modules
    - `Import` → `[Identities/Import(0), Nodes/Import(1), Teams/Import(2), WorkItems/Import(3)]` filtered to enabled modules
    - `Migrate` → Export tasks (orders 0–4) + Import tasks (orders 5–8)
    - `Inventory` → `[Inventory(order:0)]`
    - `Prepare` / `Dependencies` → single task each
  - Reads `IPhaseTrackingService.ReadPhaseRecordAsync` — marks all Export-phase tasks `Skipped` if `record.ExportCompleted`, all Import-phase tasks `Skipped` if `record.ImportCompleted`
  - Checks `IArtefactStore.ExistsAsync("inventory.json")` — if true, marks `Inventory` task `Skipped` with `SkipReason: "Completed in prior run"` and sets `KnownTotal` on downstream tasks from deserialized inventory data
  - Logs `Warning` per enabled-but-no-task module (defensive guard)
  - Constructor injects: `IPhaseTrackingServiceFactory`, `IArtefactStore`, `ILogger<JobExecutionPlanBuilder>`
- [ ] T010 Register `IJobExecutionPlanBuilder → JobExecutionPlanBuilder` as scoped in `CoreAgentServiceExtensions.AddCoreAgentServices`

---

## Phase 4: Control Plane — Storage + Endpoints (Parallel with Phase 3)

- [ ] T011 Create `src/DevOpsMigrationPlatform.ControlPlane/Store/IJobTaskStore.cs`
  — interface:
  ```csharp
  void Upsert(Guid jobId, JobTaskList tasks);
  void UpdateTask(Guid jobId, string taskId, JobTaskStatus status,
                  DateTimeOffset? startedAt, DateTimeOffset? completedAt, long? completedCount);
  JobTaskList? Get(Guid jobId);
  ```
- [ ] T012 Create `src/DevOpsMigrationPlatform.ControlPlane/Store/InMemoryJobTaskStore.cs`
  — `ConcurrentDictionary<Guid, JobTaskList>` backing; `UpdateTask` finds matching task and replaces it via `record with { }` expression; no-op if task not found (logs `Warning`)
- [ ] T013 Register `IJobTaskStore → InMemoryJobTaskStore` as singleton in the control plane host DI
- [ ] T014 Add `POST /agents/lease/{leaseId}/tasks` to `TelemetryController.cs`
  — resolves `jobId` from lease store; calls `_taskStore.Upsert(jobId, taskList)`; returns `204 No Content`; validates `taskList.Tasks.Count > 0` else `400 Bad Request`
- [ ] T015 Update `GET /jobs/{jobId}/bootstrap` in `TelemetryController.cs`
  — add `Tasks = _taskStore.Get(jobId)` to the `JobBootstrap` response
- [ ] T016 [P] Add `GET /jobs/{jobId}/tasks` to `TelemetryController.cs`
  — returns `JobTaskList` (`200`) or `204 No Content` if no task list pushed yet; direct access without full bootstrap

---

## Phase 5: Control Plane — Task State Derivation from ProgressEvents

*Depends on Phase 4 (T011–T013)*

- [ ] T017 Update `ProgressController.PostProgress`
  — after storing the event in the ring buffer, check `evt.TaskId != null && evt.TaskStatus != null`; if so:
  - `Running` → `UpdateTask(jobId, taskId, Running, startedAt: evt.Timestamp, null, null)`
  - `Completed` → `UpdateTask(jobId, taskId, Completed, null, completedAt: evt.Timestamp, completedCount: evt.CompletedCount)`
  - `Failed` → `UpdateTask(jobId, taskId, Failed, null, completedAt: evt.Timestamp, null)`
  - `Skipped` → `UpdateTask(jobId, taskId, Skipped, null, null, null)`
  - If `_taskStore.Get(jobId)` returns null or task not found, log `Warning` with `{JobId}`, `{TaskId}`, `{Module}` — do not fail the request
- [ ] T018 [P] Unit test `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Controllers/ProgressController_TaskStateTests.cs`
  — `Running` event sets `StartedAt`; `Completed` event sets `CompletedAt` + `CompletedCount`; `Failed` event sets `CompletedAt`; unknown `TaskId` logs `Warning` and returns success; null `TaskId` in event is a no-op

---

## Phase 6: Agent — Push Plan + Emit TaskId

*Depends on Phase 3 (T008–T010) and Phase 4 (T014)*

- [ ] T019 Add `PushTaskListAsync(string leaseId, JobTaskList tasks, CancellationToken ct) : Task` to `IControlPlaneTelemetryClient` (`Abstractions/ControlPlaneApi/IControlPlaneTelemetryClient.cs`)
- [ ] T020 Implement `PushTaskListAsync` in `ControlPlaneTelemetryClient.cs`
  — `POST /agents/lease/{leaseId}/tasks`; best-effort (catch `HttpRequestException`, log `Warning`; do not throw)
- [ ] T021 Update `JobAgentWorker.cs`
  - After `ActiveJobConfig.PackageConfig = packageConfig` and before creating the job scope:
    1. Resolve `IJobExecutionPlanBuilder` from root service provider
    2. Call `await planBuilder.BuildPlanAsync(packageConfig, job.Kind, ct)`
    3. Call `await _telemetryClient.PushTaskListAsync(lease.LeaseId, plan, ct)`
    4. Log `Information` with `{TaskCount}` and task ids
  - On each module's start: emit `ProgressEvent { TaskId = GetTaskId(module, phase), TaskStatus = Running }`
  - On each module's success: emit `ProgressEvent { TaskId, TaskStatus = Completed, CompletedCount = result.ItemCount }`
  - On each module's failure: emit `ProgressEvent { TaskId, TaskStatus = Failed }`
  - Add private static helper `GetTaskId(IModule module, string phase)` → `"{module.ModuleName}/{phase}"` e.g. `"WorkItems/Export"`
- [ ] T022 Update `TfsJobAgentWorker.cs`
  — same plan-build and push pattern as T021 for the TFS module execution loop; include `TaskId` and `TaskStatus` in all lifecycle `ProgressEvent` records emitted via stdout NDJSON

---

## Phase 7: Feature File Step Definitions

*Depends on Phases 2, 4, 6*

- [ ] T023 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/JobExecutionPlanSteps.cs` + `JobExecutionPlanContext.cs`
  — Reqnroll bindings for `features/platform/job-execution-plan.feature`
  — `[TestCategory("SystemTest_Simulated")]`
  — MUST assert: task list non-empty; task IDs match expected module/phase pattern; Skipped tasks have non-empty `SkipReason`; `KnownTotal` populated when inventory exists
- [ ] T024 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/TaskAttributionSteps.cs` + `TaskAttributionContext.cs`
  — Reqnroll bindings for `features/platform/task-attribution.feature`
  — `[TestCategory("SystemTest_Simulated")]`
  — MUST assert: `ProgressEvent.TaskId` matches a known task ID; task status in bootstrap transitions Running → Completed after events processed; unknown TaskId emits Warning without throwing

---

## Phase 8: Unit Tests

*Parallel with Phase 7*

- [ ] T025 [P] Unit test `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs`
  - Export job, all modules enabled → 5 tasks (Inventory + 4 Export)
  - Import job → 4 tasks (no Inventory)
  - Migrate job → 9 tasks (Inventory + 4 Export + 4 Import)
  - Export job, `record.ExportCompleted = true` → all Export tasks `Skipped`
  - `inventory.json` exists → Inventory `Skipped`, downstream `KnownTotal` populated, count > 0
  - All modules disabled → only Inventory task; `Warning` logged per disabled module
- [ ] T026 [P] Unit test `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Store/InMemoryJobTaskStoreTests.cs`
  - Upsert replaces existing task list
  - UpdateTask on non-existent `jobId` is a no-op (no exception)
  - UpdateTask on non-existent `taskId` within known job logs `Warning` and is a no-op
  - Get returns `null` when no list has been upserted

---

## Phase 9: Build + Verify

- [ ] T027 `dotnet clean DevOpsMigrationPlatform.slnx --nologo -v quiet && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — 0 errors, 0 warnings
- [ ] T028 `dotnet test DevOpsMigrationPlatform.slnx` — ALL tests pass, no filter
- [ ] T029 [Manual] Run `queue-export-simulated` launch profile; call `GET /jobs/{id}/bootstrap`; verify `tasks` array present with correct task IDs and `Pending` statuses before any module executes

---

## File Map

### New files
| File | Purpose |
|------|---------|
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTaskStatus.cs` | Enum |
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTask.cs` | Task record |
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTaskList.cs` | Task list record |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobExecutionPlanBuilder.cs` | Builder interface |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` | Builder implementation |
| `src/DevOpsMigrationPlatform.ControlPlane/Store/IJobTaskStore.cs` | Store interface |
| `src/DevOpsMigrationPlatform.ControlPlane/Store/InMemoryJobTaskStore.cs` | In-memory store |
| `features/platform/job-execution-plan.feature` | Gherkin US-1, US-3 |
| `features/platform/task-attribution.feature` | Gherkin US-2 |
| `tests/.../Platform/JobExecutionPlanSteps.cs` + `Context.cs` | Reqnroll bindings |
| `tests/.../Platform/TaskAttributionSteps.cs` + `Context.cs` | Reqnroll bindings |
| `tests/.../Context/JobExecutionPlanBuilderTests.cs` | Unit tests |
| `tests/.../Store/InMemoryJobTaskStoreTests.cs` | Unit tests |
| `tests/.../Controllers/ProgressController_TaskStateTests.cs` | Unit tests |

### Modified files
| File | Change |
|------|--------|
| `src/DevOpsMigrationPlatform.Abstractions/Streaming/ProgressEvent.cs` | Add `TaskId?`, `TaskStatus?`, `CompletedCount?` |
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobBootstrap.cs` | Add `Tasks?` |
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/IControlPlaneTelemetryClient.cs` | Add `PushTaskListAsync` |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs` | Register `IJobExecutionPlanBuilder` |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs` | Implement `PushTaskListAsync` |
| `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs` | Add `POST /tasks`, update bootstrap, add `GET /tasks` |
| `src/DevOpsMigrationPlatform.ControlPlane/Controllers/ProgressController.cs` | Task state derivation on progress POST |
| `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` | Build + push plan; emit TaskId on module lifecycle |
| `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` | Build + push plan; emit TaskId in NDJSON events |

---

## Dependencies

```
Phase 1 (Types) → all other phases
Phase 2 (Feature Files) ← parallel with Phase 3
Phase 3 (Plan Builder) → Phase 6 (Agent wiring)
Phase 4 (Control Plane) → Phase 5 (State derivation)
                        → Phase 6 (Agent client push)
Phases 4 + 6 → Phase 7 (Step definitions)
Phases 7 + 8 → Phase 9 (Build + verify)
```
