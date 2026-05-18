# Feature Specification: Job Execution By Task

**Feature Branch**: `028.2-job-execution-by-task`
**Created**: 2026-05-01
**Status**: Draft
**Depends On**: `028.1-task-bootstrap` (JobTask, JobTaskList, IJobExecutionPlanBuilder, ProgressEvent.TaskId must be in place)

**Input**: The agent builds an execution plan (spec 028.1) and pushes it to the control plane for display — but then discards it and falls back to a hardcoded `foreach (var module in jobModules)` loop. This loop is sequential, ignores `IModule.DependsOn`, provides no parallelism, silently breaks if DI registration order changes, and cannot resume at task granularity. The plan should be the authoritative execution driver.

---

## Architecture References

| Document | Status |
|----------|--------|
| `docs/architecture.md` | Confirmed — no conflicts |
| `docs/module-development-guide.md` | **Applies directly** — `IModule.DependsOn` documents the dependency graph contract; this spec realises it |
| `docs/migration-process-guide.md` | **Has gap** — does not describe plan-driven execution or per-phase parallelism; needs updating after implementation |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Rules 7 (IStateStore only), 12 (stateless agent/durable state in package), 17 (all state in checkpoints), 21 (mandatory reuse of existing architecture) apply |
| `.agents/20-guardrails/core/coding-standards.md` | Rule 8 (no `.Result`/`.Wait()`), rule 14 (resilience) apply |
| `.agents/30-context/domains/job-lifecycle.md` | Confirmed — `Job.Resume.Mode == ForceFresh` must trigger plan deletion + rebuild |
| `.agents/30-context/domains/package-manager.md` | Confirmed — plan persistence uses `IStateStore`, not `IArtefactStore` |

---

## Current status (reconciled 2026-05-17)

- Change class: **A** (documentation/status reconciliation only; no runtime surface change).
- Applicable guardrails: architecture boundaries, change governance, surface usage, testing rules, documentation rules.
- Guardrail-rejected approach: restoring legacy `.migration/Checkpoints/plan.json` + `PackagePaths.PlanFile` as a parallel plan surface was rejected; current canonical runtime path is `.migration/plan.json` via `PackageMetaKind.ExecutionPlan` and `IPackageAccess`.

### Remaining incomplete work

- T009, T016, T017, T018

### Completed because superseded

- T002 → superseded by `specs/034-package-manager-adoption/tasks.md` T042/T043/T045 (plan persistence moved to `IPackageAccess` + `PackageMetaKind.ExecutionPlan`).
- T005 → superseded by `specs/030-module-analiser-refactor/tasks.md` T016 (phase-aware dependency graph; export dependencies are no longer forced empty).

### Contradictions and reconciliation

- FR-004/plan Phase 1 references `.migration/Checkpoints/plan.json` and a non-existent `Abstractions.Agent/Lease/PackagePaths.cs`; implementation uses `.migration/plan.json` routed by `PackagePathRouter` + `PackageMetaKind.ExecutionPlan`.
- FR-005 interface shape is stale versus current `IJobPlanExecutor` (unified `ExecuteTasksAsync` + capture/analyser routing from spec 032).

### Verification evidence

- Runtime wiring: `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`, `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs`, `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`.
- Path contract: `src/DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem/PackagePathRouter.cs`, `src/DevOpsMigrationPlatform.Abstractions.Storage/PackageMetaKind.cs`.
- Tests: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs`, `.../JobExecutionPlanBuilderDependsOnTests.cs`, `.../Platform/PlanDrivenExecutionSteps.cs`, `.../ParallelModuleExecutionSteps.cs`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Agent Executes Modules in Dependency DAG Order (Priority: P1)

A platform operator submits an Import job with `devopsmigration queue --import`. The `WorkItemsModule` declares `DependsOn = ["Identities", "Nodes"]`. When the agent runs, the Identities and Nodes import modules complete first; only then does WorkItems begin. If Identities import fails, WorkItems import is automatically skipped — it is never attempted against a target with unmapped identities.

**Why this priority**: The current hardcoded `foreach` loop happens to produce the correct order only because DI registration order matches the intended execution order. This is accidental — any future refactor of the DI setup or addition of a new module could silently execute WorkItems before its dependencies, causing corrupt data in the target. Making the DAG explicit and enforced is a correctness requirement.

**Independent Test**: Submit a Simulated Import job. Inject a failure into the Identities module. Assert that WorkItems import was marked `Skipped` with `skipReason` referencing the failed dependency — never `Running` or `Failed`.

**Acceptance Scenarios**:

1. **Given** an Import job with all modules enabled, **When** the agent runs, **Then** the execution order is: Identities, Nodes, Teams (concurrent tier), then WorkItems (after Identities and Nodes both complete successfully).
2. **Given** an Import job where `IdentitiesModule.ImportAsync` throws an exception, **When** the plan executor handles the failure, **Then** the `WorkItems` import task transitions to `Skipped` with `SkipReason` containing `"Identities"`, and `Nodes` and `Teams` tasks that have no dependency on `Identities` continue to execute normally.
3. **Given** a module with `DependsOn = ["Identities"]` and the `Identities` module is disabled in configuration, **When** the agent builds and executes the plan, **Then** the dependent module is also marked `Skipped` with a `SkipReason` indicating `"Identities"` is not available.
4. **Given** a circular dependency between two modules (e.g. `A` depends on `B` and `B` depends on `A`), **When** the plan executor builds the execution tier list, **Then** it throws an `InvalidOperationException` with a message identifying the cycle, and the job is marked `Failed` before any module executes.
5. **Given** an Export job, **When** the agent runs, **Then** all export modules (Identities, Nodes, Teams, WorkItems) run concurrently — all export tasks have no declared dependencies for the export phase.

---

### User Story 2 — Independent Modules Run in Parallel (Priority: P1)

A platform operator runs a Migrate job over a large project. The Identities, Nodes, and Teams export operations run simultaneously, each writing to their own package folder. The operator watching the CLI progress display sees three progress bars advancing at the same time. Total job time is reduced because three source-system reads overlap.

**Why this priority**: The current sequential loop introduces unnecessary latency. Export operations read from the source system independently — there is no data dependency between them. Parallelising Tier 0 export alone (Identities + Nodes + Teams + WorkItems all independent) provides immediate throughput gains. On import, parallelising Tier 0 (Identities + Nodes + Teams) before Tier 1 (WorkItems) is a correctness-improving change that also provides a performance benefit.

**Independent Test**: Submit a Simulated Export job. Record the `StartedAt` timestamp of the `Identities`, `Nodes`, and `Teams` tasks from the progress stream. Assert that at least two of these three tasks have overlapping execution windows (their `StartedAt` timestamps are within 500 ms of each other).

**Acceptance Scenarios**:

1. **Given** an Export job with Identities, Nodes, Teams, and WorkItems all enabled, **When** the agent executes the Export phase, **Then** all four tasks start within 500 ms of each other (run as a single concurrent tier).
2. **Given** an Import job with all modules enabled, **When** the agent executes the Import phase, **Then** Identities, Nodes, and Teams tasks all have overlapping `StartedAt`/`CompletedAt` windows; WorkItems has a `StartedAt` that is no earlier than the `CompletedAt` of both Identities and Nodes.
3. **Given** a task that fails during parallel execution, **When** the failure occurs, **Then** other tasks in the same tier that have already started are allowed to complete (no cancellation of sibling tasks); tasks in subsequent tiers that depend on the failed task are skipped.
4. **Given** the `CancellationToken` is cancelled mid-execution, **When** a parallel tier is running, **Then** all running tasks in that tier receive the cancellation and stop as promptly as their implementation allows; no `OperationCanceledException` is swallowed.

---

### User Story 3 — Plan Persisted to Package, Resumed on Agent Restart (Priority: P1)

An agent is mid-way through an Import job — Identities and Nodes are `Completed`, WorkItems is `Running` — when the process crashes. A new agent picks up the job. It reads `.migration/Checkpoints/plan.json` from the package, sees the persisted task statuses, marks the crashed `Running` task as `Pending` again (incomplete), and resumes from there. Identities and Nodes are not re-imported — their `Completed` status in the persisted plan (corroborated by their module cursors) is sufficient to satisfy WorkItems' dependency.

**Why this priority**: Spec 028.1 pushes the plan to the control plane for display, but the plan lives only in memory. If the agent crashes, the plan is gone and the new agent re-executes everything (or uses the coarse `JobPhaseRecord` which only has phase-level granularity). Persisting the plan to the package means a restart is fully informed about exactly which tasks already completed, enabling fine-grained resume without redundant work.

**Independent Test**: Submit a Simulated Export job. After the first module completes, simulate a crash by reading `.migration/Checkpoints/plan.json` from the package and verifying that the completed module's task has `status: Completed`. Submit a new agent run for the same job without `--force-fresh`. Assert that the completed module is not re-executed (its `ExportAsync` is not called a second time).

**Acceptance Scenarios**:

1. **Given** a job begins executing, **When** the first module completes, **Then** `.migration/Checkpoints/plan.json` exists in the package and contains the task with `status: Completed` and a non-null `CompletedAt` timestamp.
2. **Given** a plan file exists in the package with tasks `Completed` and `Running`, **When** a new agent picks up the same job, **Then** `Completed` tasks are not re-executed; `Running` tasks (which crashed mid-way) are reset to `Pending` and re-executed; subsequent task dependencies are re-evaluated from the reset state.
3. **Given** a `ForceFresh` resume mode, **When** the agent begins the job, **Then** `.migration/Checkpoints/plan.json` is deleted, module cursors are deleted, and a fresh plan is built and persisted before any module executes.
4. **Given** no `.migration/Checkpoints/plan.json` exists (first run), **When** the agent begins the job, **Then** the plan is built via `IJobExecutionPlanBuilder.BuildPlanAsync` and immediately persisted before the first module executes.
5. **Given** `.migration/Checkpoints/plan.json` exists and a compatible plan was pushed to the control plane in a prior run, **When** the new agent pushes the reloaded plan via `IControlPlaneTelemetryClient.PushTaskListAsync`, **Then** the control plane replaces its in-memory task list with the reloaded one (idempotent upsert).

---

## Edge Cases

- **`DependsOn` references a non-existent module name**: If a module declares `DependsOn = ["NonExistentModule"]` and no module with that name is registered, the plan executor logs a `Warning` and treats the dependency as satisfied (absent modules cannot block execution). This is not a fatal error — the module may have been disabled or removed.
- **All modules in a phase are skipped**: If every task in a phase is `Skipped` (all disabled or all previously completed), the phase executor completes immediately with success and logs an `Information` message noting that the phase was a no-op.
- **`Running` tasks in persisted plan on resume**: A `Running` task in the plan file indicates an in-flight task that never completed (agent crash). On load, the plan executor resets all `Running` tasks to `Pending` before evaluating the dependency graph. This ensures partial work is always retried.
- **Plan file corrupt/unreadable**: If `IStateStore.ReadAsync(PackagePaths.PlanFile)` returns data that cannot be deserialised, the executor logs a `Warning` and falls back to building a fresh plan via `IJobExecutionPlanBuilder.BuildPlanAsync`. This is safe: module cursors are intact from the prior run, so individual module resume still works.
- **TFS subprocess (net481)**: The TFS export agent does not use `IJobPlanExecutor` directly — it has a single `TfsWorkItemsExportModule` and runs via `TfsJobAgentWorker`. However, it must update the persisted plan file (mark its task `Running` on start and `Completed`/`Failed` on finish). `PackagePaths.PlanFile` is accessible from net481 (defined in multi-targeted `Abstractions.Agent`). The TFS worker reads and writes the plan file via `IStateStore`.
- **Migrate mode with Export already completed**: On resume of a Migrate job, the plan file has Export-phase tasks `Completed`. The executor skips all Export tasks and proceeds directly to Import. The coarse `JobPhaseRecord.ExportCompleted` is still written for backward compatibility but the plan file is now the authoritative record.
- **`DependsOn` is evaluated per-phase**: A module's `DependsOn` applies to Import only. During the Export phase, all modules are independent (no module reads another module's package output during export). The plan builder must produce `DependsOn = []` for all Export-phase tasks regardless of the module's declared `DependsOn`.

---

## Observability

### Operations

| Name | Type | Entry Point | Observable Boundary |
|------|------|-------------|---------------------|
| `job.plan.execute.tier` | span per tier | `JobPlanExecutor` | One span per execution tier; tagged with tier index, task count, phase |
| `job.plan.persist` | log only | `JobPlanExecutor` | `Information` when plan written; `Warning` when plan unreadable |
| `job.task.skip.dependency` | log only | `JobPlanExecutor` | `Warning` per task skipped due to failed/missing dependency |

### O-1 Traces

- `job.plan.execute.tier`: `ActivitySource.StartActivity("job.plan.execute.tier")` per tier. Tags: `job.phase` (`Export`/`Import`), `job.tier_index` (int), `job.tier_task_count` (int). Span covers `Task.WhenAll` duration. Source: `WellKnownActivitySourceNames.Migration`.
- Task-level spans: reuse the existing module-level spans that each module already emits. No new spans per task inside the executor.

### O-2 Metrics

No new metric instruments. Tier-level throughput is visible from per-module metrics already instrumented in each module.

### O-3 Structured Logging

| Event | Level | Fields |
|-------|-------|--------|
| Plan loaded from package | `Information` | `jobId`, `taskCount`, `pendingCount`, `completedCount`, `skippedCount` |
| Plan built fresh (no prior plan) | `Information` | `jobId`, `taskCount`, `tierCount` |
| Plan written to package | `Information` | `jobId`, `path` |
| Plan unreadable — falling back to fresh build | `Warning` | `jobId`, `path`, error message |
| Tier execution starting | `Information` | `jobId`, `phase`, `tierIndex`, `taskIds` |
| Tier execution complete | `Information` | `jobId`, `phase`, `tierIndex`, `succeeded`, `failed`, `skipped` |
| Task skipped — dependency failed or disabled | `Warning` | `jobId`, `taskId`, `dependencyName` |
| Circular dependency detected | `Error` | `jobId`, cycle description |
| `Running` task reset to `Pending` on load | `Information` | `jobId`, `taskId` |

### O-4 Progress Events

The plan executor emits `ProgressEvent` with `TaskId` and `TaskStatus` on every task lifecycle transition, using the same pattern as spec 028.1:
- Task starts: `TaskStatus = Running`
- Task completes: `TaskStatus = Completed`, `CompletedCount` from module result
- Task fails: `TaskStatus = Failed`
- Task skipped by dependency: `TaskStatus = Skipped`

---

## Connector Coverage

| Concern | Simulated | AzureDevOps | TFS |
|---------|-----------|-------------|-----|
| Export: all tasks run concurrently (tier 0) | Required | Required | Required (TfsWorker writes plan; single module but plan file still persisted) |
| Import: Tier 0 parallel (Identities/Nodes/Teams), Tier 1 serial (WorkItems after deps) | Required | Required | N/A (TFS is source-only) |
| Plan persisted to `.migration/Checkpoints/plan.json` | Required | Required | Required (TfsWorker writes Running/Completed to plan) |
| Resume from persisted plan | Required | Required | Required |
| `Running` tasks reset to `Pending` on load | Required | Required | Required |

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `JobTask` MUST gain an additive `DependsOn: IReadOnlyList<string>?` field (nullable, defaults to null = no dependencies). Null and empty list are semantically equivalent.
- **FR-002**: `JobExecutionPlanBuilder` MUST receive `IEnumerable<IModule>` via constructor injection. When building tasks for the Import phase, it MUST map each module's `IModule.DependsOn` names onto the corresponding `JobTask.DependsOn` field as `import.{name}` task IDs. For Export-phase tasks, `DependsOn` MUST always be empty regardless of the module's declared dependencies.
- **FR-003**: `JobExecutionPlanBuilder.BuildPlanAsync` MUST perform a topological sort of the Import-phase tasks and detect circular dependencies. A circular dependency MUST throw `InvalidOperationException` identifying the cycle — this is a fatal configuration error.
- **FR-004**: A new constant `PackagePaths.PlanFile = ".migration/Checkpoints/plan.json"` MUST be added to `PackagePaths`.
- **FR-005**: A new `IJobPlanExecutor` interface MUST be defined in `DevOpsMigrationPlatform.Abstractions.Agent`. It MUST expose two methods: `ExecuteExportPhaseAsync` and `ExecuteImportPhaseAsync`. Both accept the `JobTaskList`, a dictionary of modules keyed by `IModule.Name`, the appropriate execution context, an `IStateStore` for plan persistence, and a `CancellationToken`.
- **FR-006**: `JobPlanExecutor` MUST execute all tasks within a tier concurrently via `Task.WhenAll`. A tier is a maximal set of tasks whose `DependsOn` tasks have all already completed or been skipped. Tasks in the same tier start simultaneously.
- **FR-007**: If a task's dependency task has `Status == Failed` or `Status == Skipped` (due to a prior failure or disabled module), the dependent task MUST be transitioned to `Skipped` with a `SkipReason` naming the dependency — it MUST NOT be executed.
- **FR-008**: `JobPlanExecutor` MUST persist the `JobTaskList` to `IStateStore` at `PackagePaths.PlanFile` after every task status transition. The plan file is the single durable record of execution progress.
- **FR-009**: On startup, before executing any tier, the executor MUST attempt to load the plan from `IStateStore`. If found: all `Running` tasks are reset to `Pending`. If not found: the plan is the fresh plan passed in (already built by `IJobExecutionPlanBuilder`).
- **FR-010**: On `ForceFresh`: `JobAgentWorker` MUST delete `PackagePaths.PlanFile` from `IStateStore` before calling `BuildPlanAsync`, ensuring a clean starting state.
- **FR-011**: `JobAgentWorker.OnMigrationJobAsync` MUST be refactored to replace both `foreach (var module in jobModules)` loops with a single call to `IJobPlanExecutor.ExecuteExportPhaseAsync` and/or `IJobPlanExecutor.ExecuteImportPhaseAsync`. The hardcoded sequential loops MUST be removed.
- **FR-012**: The `JobPlanExecutor` MUST NOT cancel sibling tasks when one task in a tier fails. Siblings continue to completion; only tasks in subsequent tiers that depend on the failed task are skipped.
- **FR-013**: Every `ProgressEvent` emitted by the plan executor for task lifecycle transitions MUST carry `TaskId` (matching the `JobTask.Id`) and `TaskStatus`. The `CompletedCount` field MUST be set on `Completed` events where the module returns a count.
- **FR-014**: The `TfsJobAgentWorker` MUST be updated to read and write `.migration/Checkpoints/plan.json` at the task level — marking its task `Running` on start and `Completed`/`Failed` on finish. It does not use `IJobPlanExecutor` for execution (single module), but it MUST update the plan file so a resume-aware main agent can see TFS task completion status.

### Key Entities

- **`JobTask.DependsOn`**: `IReadOnlyList<string>?` — task IDs (e.g. `"import.identities"`) that must complete before this task may execute. Null/empty = no dependencies.
- **`IJobPlanExecutor`**: Service in `Abstractions.Agent` (multi-targeted). Methods: `ExecuteExportPhaseAsync(plan, modules, exportContext, stateStore, ct)` and `ExecuteImportPhaseAsync(plan, modules, importContext, stateStore, ct)`. Both return `bool` (true = all executed tasks succeeded, false = at least one failed).
- **`JobPlanExecutor`**: Implementation in `Infrastructure.Agent`. Performs topological tier extraction, `Task.WhenAll` within tiers, plan persistence after each transition, `ProgressEvent` emission.
- **`PackagePaths.PlanFile`**: Constant `".migration/Checkpoints/plan.json"` — the durable plan state key.

---

## Success Criteria *(mandatory)*

1. A Simulated Import job with all modules enabled shows Identities, Nodes, and Teams tasks with overlapping `StartedAt` timestamps in the `plan.json`, and WorkItems `StartedAt` is after both Identities and Nodes `CompletedAt`.
2. A Simulated Import job where Identities fails: WorkItems task in `plan.json` has `Status: Skipped` with a non-empty `SkipReason`; Nodes and Teams ran and completed normally.
3. After any module completes, `.migration/Checkpoints/plan.json` exists in the package with the correct task status.
4. A simulated crash-and-resume (delete the plan file entry from state, re-run without `ForceFresh`) causes completed modules to not be re-executed; the resumed run completes successfully.
5. `dotnet clean && dotnet build --no-incremental` — 0 errors.
6. All tests pass: `dotnet test DevOpsMigrationPlatform.slnx`.

