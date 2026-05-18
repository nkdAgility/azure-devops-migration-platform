# Implementation Plan: Job Execution By Task

**Branch**: `028.2-job-execution-by-task` | **Date**: 2026-05-01 | **Spec**: [spec.md](spec.md)

## Summary

Replace the hardcoded sequential `foreach` loops in `JobAgentWorker.OnMigrationJobAsync` with a plan-driven executor that:
1. Adds `DependsOn: IReadOnlyList<string>?` to `JobTask` (additive, no breaking change).
2. Injects `IEnumerable<IModule>` into `JobExecutionPlanBuilder` so it maps `IModule.DependsOn` names onto Import-phase task IDs.
3. Adds `PackagePaths.PlanFile` constant (`".migration/Checkpoints/plan.json"`).
4. Introduces `IJobPlanExecutor` (Abstractions.Agent) + `JobPlanExecutor` (Infrastructure.Agent) — topological tier sort with `Task.WhenAll` per tier, plan persistence after each transition.
5. Updates `JobAgentWorker` to call `IJobPlanExecutor.ExecuteExportPhaseAsync` and `IJobPlanExecutor.ExecuteImportPhaseAsync` instead of the foreach loops.
6. Updates `TfsJobAgentWorker` to write task status updates to the persisted plan.

**Result**: Export runs 4 modules concurrently. Import runs Identities/Nodes/Teams concurrently, then WorkItems. Plan is durable in the package; crashed agents resume at task granularity. Circular dependency detection is enforced at plan-build time.

## Current status (reconciled 2026-05-17)

- Canonical task status is tracked in `tasks.md` (created during this reconciliation).
- Plan text below is preserved as historical implementation intent; status truth is `tasks.md`.

### Remaining incomplete work

- T009, T016, T017, T018

### Completed because superseded

- T002 superseded by `specs/034-package-manager-adoption/tasks.md` T042/T043/T045.
- T005 superseded by `specs/030-module-analiser-refactor/tasks.md` T016.

### Contradictions and reconciliation

- `PackagePaths.PlanFile` path in this plan is stale; runtime now uses `PackageMetaKind.ExecutionPlan` routed to `.migration/plan.json`.
- `IJobPlanExecutor` signatures in this plan are stale; current contract includes unified `ExecuteTasksAsync` and capture/analyser routing updates from spec 032.
- Lifetime note in T009 (`singleton`) does not match current registration (`AddScoped`).

### Verification evidence

- Commands: `dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo -v minimal` (succeeded with warnings), targeted `dotnet test` for plan-execution tests (39 passed).
- Implementation files: `JobAgentWorker.cs`, `JobPlanExecutor.cs`, `JobExecutionPlanBuilder.cs`, `TfsJobAgentWorker.cs`, `CoreAgentServiceExtensions.cs`, `PackagePathRouter.cs`.

## Technical Context

**Language/Version**: C# 12, .NET 10 (agent + control plane); .NET 4.8 (TFS agent — `Abstractions.Agent` and `PackagePaths` are multi-targeted)
**Primary Dependencies**: `IModule.DependsOn` (already declared), `IStateStore` (already available), `IJobExecutionPlanBuilder` (spec 028.1), `JobTaskList`/`JobTask`/`JobTaskStatus` (spec 028.1)
**Prerequisite**: Spec 028.1 types (`JobTask`, `JobTaskList`, `JobTaskStatus`, `ProgressEvent.TaskId`, `IJobExecutionPlanBuilder`) must be merged before any phase here begins.
**No breaking changes**: `JobTask.DependsOn` is additive nullable; `JobExecutionPlanBuilder` constructor gains `IEnumerable<IModule>` which is satisfied by existing DI registration; `IJobPlanExecutor` is a new interface.

## Constitution Check

- [x] **Package-First (I)**: Plan written to `IStateStore` at `PackagePaths.PlanFile` — correct interface, correct path prefix. No direct filesystem access.
- [x] **Streaming (II/III)**: Not applicable — orchestration concern, not module data processing.
- [x] **Cursor Checkpoints (IV)**: Plan file is the task-level checkpoint. Module cursors (`.migration/Checkpoints/<module>.cursor.json`) are unchanged and still provide resume-within-module granularity.
- [x] **Module Isolation (V)**: `IJobPlanExecutor` calls modules via existing `IModule.ExportAsync`/`ImportAsync` — no new coupling introduced.
- [x] **Separation of Planes (VI)**: Execution logic stays in the agent (`JobAgentWorker`, `JobPlanExecutor`). Control plane receives plan status updates via existing `ProgressEvent` → `IJobTaskStore` path (spec 028.1). CLI/TUI unchanged.
- [x] **Determinism (VII)**: Topological tier order is deterministic for a given `DependsOn` graph. `Task.WhenAll` start order is non-deterministic but module operations are idempotent — no correctness dependency on sibling start order.
- [x] **ATDD-First (VIII)**: Feature files written in Phase 2 before implementation phases begin.
- [x] **SOLID & DI (IX)**: `IJobPlanExecutor` in `Abstractions.Agent`; `JobPlanExecutor` in `Infrastructure.Agent`; registered via `AddCoreAgentServices`. Constructor injection throughout.
- [x] **Reuse (XXI)**: `IJobPlanExecutor` calls the existing `IModule.ExportAsync`/`ImportAsync` methods and emits existing `ProgressEvent` structures. No new abstraction duplicates existing patterns.

## Observability Contract

| Operation | Span (O-1) | Metrics (O-2) | Logs (O-3) | Progress (O-4) |
|-----------|-----------|--------------|-----------|----------------|
| `job.plan.execute.tier` | `StartActivity("job.plan.execute.tier")` with `job.phase`, `job.tier_index`, `job.tier_task_count` | None (module metrics unchanged) | `Information`: "Executing tier {TierIndex} ({TaskCount} tasks) for {Phase}" | N/A — executor drives per-task ProgressEvents |
| Per-task lifecycle | None (reuses module's own span) | None | `Information` start/complete; `Warning` skip; `Error` fail | `ProgressEvent` with `TaskId` + `TaskStatus` on Running/Completed/Failed/Skipped |
| Plan I/O | None | None | `Information` load/save; `Warning` corrupt/fallback | N/A |

---

## Phase 1: Extend `JobTask` + Add `PackagePaths.PlanFile` (Blocking Prerequisite)

All phases depend on this. Additive changes only — no existing code breaks.

- [ ] T001 Update `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTask.cs`
  — add `public IReadOnlyList<string>? DependsOn { get; init; }` (nullable, `init`-only, after `SkipReason`). Default is null (no dependencies). Serialises cleanly to/from JSON — `null` and missing key are equivalent on deserialise.
- [ ] T002 Add `public const string PlanFile = $"{Checkpoints}/plan.json";` to `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs`
  — path: `.migration/Checkpoints/plan.json`. Place after `PhaseFile` constant for logical grouping.

**Checkpoint**: `dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — 0 errors; no existing tests broken.

---

## Phase 2: Feature Files (Parallel with Phase 3)

Gherkin scenarios lock in the acceptance contract before implementation. Written in plain English — step definitions follow in Phase 6.

- [ ] T003 [P] Create `features/platform/plan-driven-execution.feature`
  — tag `@platform`; scenarios for US-1 (DAG order) and US-3 (plan persistence + resume):
  - `Scenario: Import executes in dependency order` — Identities, Nodes, Teams in parallel, then WorkItems
  - `Scenario: Failed dependency causes dependent task to be skipped`
  - `Scenario: Disabled dependency causes dependent task to be skipped`
  - `Scenario: Circular dependency detected before any module executes`
  - `Scenario: Plan file written to package after first module completes`
  - `Scenario: Running tasks reset to Pending on resume`
  - `Scenario: Completed tasks not re-executed on resume`
  - `Scenario: ForceFresh deletes plan file and rebuilds`
- [ ] T004 [P] Create `features/platform/parallel-module-execution.feature`
  — tag `@platform`; scenarios for US-2 (parallelism):
  - `Scenario: All export tasks start within the same tier`
  - `Scenario: Import tier-0 tasks start concurrently before WorkItems`
  - `Scenario: CancellationToken cancels all running tier tasks`
  - `Scenario: Failed task does not cancel sibling tasks in the same tier`

---

## Phase 3: `JobExecutionPlanBuilder` — DependsOn Mapping + Topological Sort (Depends on Phase 1)

Extends the existing builder (spec 028.1) without changing the `IJobExecutionPlanBuilder` interface signature.

- [ ] T005 Update `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
  — add constructor parameter `IEnumerable<IModule> modules`; build a `_modulesByName` dictionary from it at construction time
  — in `BuildImportTasks`: for each module's `JobTask`, set `DependsOn = module.DependsOn.Select(dep => $"import.{dep.ToLowerInvariant()}").ToArray()` (only modules that are enabled/in the plan have task IDs; if a dependency name has no corresponding task in the plan, log a `Warning` and omit it from `DependsOn`)
  — in `BuildExportTasks`: always set `DependsOn = null` regardless of module's `IModule.DependsOn`
  — add `ValidateNoCycles(List<JobTask> importTasks)` private method — Kahn's algorithm topological sort over the `DependsOn` graph; throw `InvalidOperationException("Circular dependency detected: {cycle}")` if a cycle exists
  — call `ValidateNoCycles` at the end of `BuildPlanAsync` before returning
- [ ] T006 Update `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs`
  — the `JobExecutionPlanBuilder` registration already uses `AddSingleton<IJobExecutionPlanBuilder, JobExecutionPlanBuilder>()`; DI automatically injects `IEnumerable<IModule>` since modules are registered as singletons — no registration change required
  — verify by grep that the `JobExecutionPlanBuilder` constructor signature compiles against the updated registration

---

## Phase 4: `IJobPlanExecutor` Interface (Depends on Phase 1)

New interface in `Abstractions.Agent`, accessible from both net10.0 and net481.

- [ ] T007 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs`
  ```csharp
  /// <summary>
  /// Executes a <see cref="JobTaskList"/> phase using a topological tier sort,
  /// running independent tasks in parallel and persisting task status to the package
  /// after every transition. Circular dependencies throw before any task executes.
  /// </summary>
  public interface IJobPlanExecutor
  {
      /// <summary>
      /// Executes all Export-phase tasks in <paramref name="plan"/> concurrently
      /// (Export tasks have no inter-module dependencies).
      /// Returns true if all executed tasks succeeded; false if any failed.
      /// </summary>
      Task<bool> ExecuteExportPhaseAsync(
          JobTaskList plan,
          IReadOnlyDictionary<string, IModule> modulesByName,
          ExportContext exportContext,
          IStateStore stateStore,
          CancellationToken ct);

      /// <summary>
      /// Executes Import-phase tasks in dependency-tier order, running tasks
      /// within each tier concurrently via Task.WhenAll.
      /// Returns true if all executed tasks succeeded; false if any failed.
      /// </summary>
      Task<bool> ExecuteImportPhaseAsync(
          JobTaskList plan,
          IReadOnlyDictionary<string, IModule> modulesByName,
          ImportContext importContext,
          IStateStore stateStore,
          CancellationToken ct);
  }
  ```
  — multi-targeted (`net481;net10.0`); no C# 11+ features; references only `Abstractions.Agent` types

---

## Phase 5: `JobPlanExecutor` Implementation (Depends on Phases 3, 4)

Core logic: tier extraction, concurrent execution, plan persistence, ProgressEvent emission, resume handling.

- [ ] T008 Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs`
  — sealed class implementing `IJobPlanExecutor`; constructor injects `IProgressSink? progressSink`, `ILogger<JobPlanExecutor> logger`

  **`ExecuteExportPhaseAsync`**:
  - Filter plan tasks to `Phase == "Export"` and `Status != Skipped && Status != Completed`
  - All export tasks are independent (no `DependsOn`) — single tier; call `ExecuteTierAsync(tier, ...)`
  - Wrap in `ActivitySource.StartActivity("job.plan.execute.tier")` with tags `job.phase=Export`, `job.tier_index=0`, `job.tier_task_count=count`
  - Return `true` if no task failed

  **`ExecuteImportPhaseAsync`**:
  - Filter plan tasks to `Phase == "Import"` and `Status != Skipped && Status != Completed`
  - Build tier list via `ExtractTiers(tasks)` — Kahn's algorithm; already-completed dependencies (from persisted plan) count as satisfied
  - For each tier: `ActivitySource.StartActivity("job.plan.execute.tier")` with phase/tier tags, then `ExecuteTierAsync(tier, ...)`
  - After each tier: check for failed tasks; mark dependent tasks in subsequent tiers as `Skipped` with `SkipReason = "Dependency '{name}' failed or was skipped"`; persist updated plan
  - Return `true` if no task failed

  **`ExecuteTierAsync`** (private):
  - For each task in the tier, start a `Task` that:
    1. Transitions task to `Running`; persists plan; emits `ProgressEvent { TaskId, TaskStatus=Running }`
    2. Resolves `modulesByName[taskModuleName]` (where `taskModuleName` is derived from task ID e.g. `"export.workitems"` → `"WorkItems"`)
    3. Calls `module.ExportAsync(exportContext, ct)` or `module.ImportAsync(importContext, ct)`
    4. On success: transitions to `Completed`; persists plan; emits `ProgressEvent { TaskId, TaskStatus=Completed, CompletedCount }`
    5. On `OperationCanceledException`: re-throws (do not mark `Failed` — job is being cancelled)
    6. On other exception: logs `Error`; transitions to `Failed`; persists plan; emits `ProgressEvent { TaskId, TaskStatus=Failed }`; does NOT rethrow (other siblings in tier continue)
  - `Task.WhenAll(tierTasks)` — awaits all tasks in tier before returning
  - Return list of failed task IDs

  **`PersistPlanAsync`** (private):
  - `IStateStore.WriteAsync(PackagePaths.PlanFile, JsonSerializer.Serialize(plan), ct)`
  - On exception: log `Warning` — do not rethrow (plan persistence failure must not abort module execution)

  **`LoadOrResetAsync`** (private static):
  - Reads `PackagePaths.PlanFile` from `IStateStore`; deserialises
  - Resets any `Running` tasks to `Pending` (crash recovery)
  - On read/deserialise failure: logs `Warning`; returns null (caller falls back to fresh plan)

  **`ExtractTiers`** (private static):
  - Kahn's topological sort on the `DependsOn` graph built from the filtered task list
  - Returns `IReadOnlyList<IReadOnlyList<JobTask>>` — each inner list is one concurrent tier
  - Tasks whose dependency is not in the filtered list (dependency was already `Completed` or `Skipped` in the persisted plan) have their dependency treated as satisfied
  - Throws `InvalidOperationException` if cycle detected (defensive — builder should have already caught this)

  **`GetModuleName`** (private static helper):
  - Maps task ID `"export.workitems"` → `"WorkItems"`, `"import.identities"` → `"Identities"`, etc.
  - Convention: task ID = `"{phase}.{moduleName.ToLowerInvariant()}"` — strip phase prefix and restore capitalisation via dictionary lookup from `modulesByName.Keys`

- [ ] T009 Register `IJobPlanExecutor → JobPlanExecutor` as singleton in `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs`

---

## Phase 6: `JobAgentWorker` — Replace Foreach Loops (Depends on Phase 5)

This is the integration point where the new executor replaces the legacy loops.

- [ ] T010 Update `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
  — inject `IJobPlanExecutor planExecutor` in constructor (add to parameter list and field)
  — in `OnMigrationJobAsync`, after the plan is built and pushed to the control plane:
    1. Attempt to load persisted plan: `var loadedPlan = await JobPlanExecutor.LoadOrResetAsync(stateStore, ct)` — if non-null, use it (it has task status from prior run); otherwise persist the fresh plan immediately: `await PersistPlanAsync(freshPlan, stateStore, ct)`
    2. Set `var executionPlan = loadedPlan ?? freshPlan`
    3. Re-push `executionPlan` to the control plane (covers the resume-with-loaded-plan case)
  — replace the Export `foreach` block with:
    ```csharp
    if (runExport)
    {
        var moduleMap = jobModules.ToDictionary(m => m.Name, m => m);
        var exportOk = await _planExecutor.ExecuteExportPhaseAsync(
            executionPlan, moduleMap, exportContext, stateStore, ct).ConfigureAwait(false);
        failed = !exportOk;
        if (isBoth && exportOk)
            await phaseTracker.WritePhaseRecordAsync(...ExportCompleted = true..., ct).ConfigureAwait(false);
    }
    ```
  — replace the Import `foreach` block with the equivalent `ExecuteImportPhaseAsync` call
  — remove the inline `ProgressSink.Emit` calls for `Export.Start`/`Export.Complete`/`Export.Failed`/`Import.Start`/`Import.Complete`/`Import.Failed` from both former loops — the executor now emits these
  — `ForceFresh` block: after deleting cursors and phase record, also delete `stateStore.DeleteAsync(PackagePaths.PlanFile, ct)` (swallow `FileNotFoundException` / not-found)
- [ ] T011 Update `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` (net481)
  — After acquiring artefact/state stores, read plan: if `PackagePaths.PlanFile` exists, load it; find the TFS task (e.g. `"export.workitems"`)
  — Before calling module: write task to `Running`, persist plan
  — After module completes: write task to `Completed`/`Failed`, persist plan
  — This is a best-effort update — if the plan file doesn't exist (standalone TFS run), skip silently

---

## Phase 7: Step Definitions + Unit Tests (Depends on Phases 5, 6; Parallel with each other)

- [ ] T012 Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PlanDrivenExecutionSteps.cs` + `PlanDrivenExecutionContext.cs`
  — Reqnroll bindings for `features/platform/plan-driven-execution.feature`
  — `[TestCategory("SystemTest_Simulated")]` on the class
  — Context: `InMemoryStateStore` (or mock `IStateStore`), registered modules via DI, `IJobPlanExecutor`
  — MUST assert: plan file exists in state store after first task completes; task statuses match expected; skipped dependents have non-empty `SkipReason`; completed tasks not re-executed on resume
- [ ] T013 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/ParallelModuleExecutionSteps.cs` + `ParallelModuleExecutionContext.cs`
  — Reqnroll bindings for `features/platform/parallel-module-execution.feature`
  — `[TestCategory("SystemTest_Simulated")]`
  — Parallelism assertion: record `DateTime.UtcNow` at start of each simulated module; assert overlapping windows for tier-0 tasks; assert WorkItems `StartedAt` ≥ max(Identities `CompletedAt`, Nodes `CompletedAt`)
- [ ] T014 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs`
  — Plain MSTest unit tests for `JobPlanExecutor` in isolation
  - `ExecuteExportPhaseAsync_AllModulesEnabled_RunsConcurrently`: assert all 4 tasks started (using a `CountdownEvent` or `SemaphoreSlim` to detect overlap)
  - `ExecuteImportPhaseAsync_WorkItemsDependsOnIdentities_WaitsForIdentities`: mock Identities to complete before WorkItems can start; assert ordering
  - `ExecuteImportPhaseAsync_IdentitiesFails_WorkItemsSkipped`: mock Identities to throw; assert WorkItems `Status == Skipped`
  - `ExecuteImportPhaseAsync_DisabledDependency_DependentSkipped`: plan has Identities `Skipped`; assert WorkItems `Skipped` with non-empty `SkipReason`
  - `ExecuteExportPhaseAsync_CancellationToken_PropagatedToModules`: cancel after first task starts; assert remaining tasks receive cancellation
  - `ExecuteImportPhaseAsync_FailedTaskDoesNotCancelSiblings`: Nodes fails; Identities and Teams still complete
  - `PlanPersisted_AfterEachTaskTransition`: verify `IStateStore.WriteAsync` called after `Running`, `Completed`, `Failed` transitions
  - `LoadOrReset_RunningTaskResetToPending`: plan has one `Running` task; after load, task is `Pending`
  - `LoadOrReset_CorruptFile_ReturnsNull`: deserialisation failure returns null, no exception
- [ ] T015 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderDependsOnTests.cs`
  — Unit tests for the `DependsOn` mapping and cycle detection in `JobExecutionPlanBuilder`
  - `BuildPlanAsync_ImportPhase_WorkItemsDependsOnIdentitiesAndNodes`: assert `import.workitems.DependsOn` contains `"import.identities"` and `"import.nodes"`
  - `BuildPlanAsync_ExportPhase_NoDependsOn`: all export tasks have null/empty `DependsOn`
  - `BuildPlanAsync_CircularDependency_ThrowsInvalidOperationException`: inject mock modules with circular `DependsOn`; assert `InvalidOperationException` with cycle in message
  - `BuildPlanAsync_DependencyOnDisabledModule_DependencyOmittedFromTask`: module B depends on disabled module A; `import.b.DependsOn` omits `"import.a"` (and executor will not skip B on that basis)

---

## Phase 8: Build + Verify

- [ ] T016 `dotnet clean DevOpsMigrationPlatform.slnx --nologo -v quiet && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — 0 errors, 0 warnings
- [ ] T017 `dotnet test DevOpsMigrationPlatform.slnx` — ALL tests pass, no filter
- [ ] T018 [Manual] Run `queue-export-simulated` launch profile; observe CLI progress display — all 4 Export tasks visible and start within 500 ms of each other; `.migration/Checkpoints/plan.json` exists in package after job completes with all tasks `Completed`

---

## File Map

### New files

| File | Purpose |
|------|---------|
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs` | Executor interface (multi-targeted) |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` | Executor implementation |
| `features/platform/plan-driven-execution.feature` | Gherkin US-1, US-3 |
| `features/platform/parallel-module-execution.feature` | Gherkin US-2 |
| `tests/.../Platform/PlanDrivenExecutionSteps.cs` | Reqnroll step definitions |
| `tests/.../Platform/PlanDrivenExecutionContext.cs` | Shared context for above |
| `tests/.../Platform/ParallelModuleExecutionSteps.cs` | Reqnroll step definitions |
| `tests/.../Platform/ParallelModuleExecutionContext.cs` | Shared context for above |
| `tests/.../Context/JobPlanExecutorTests.cs` | Unit tests for executor |
| `tests/.../Context/JobExecutionPlanBuilderDependsOnTests.cs` | Unit tests for DependsOn mapping + cycle detection |

### Modified files

| File | Change |
|------|--------|
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTask.cs` | Add `DependsOn: IReadOnlyList<string>?` |
| `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs` | Add `PlanFile` constant |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` | Add `IEnumerable<IModule>` injection; map DependsOn; cycle detection |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs` | Register `IJobPlanExecutor → JobPlanExecutor` |
| `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` | Inject `IJobPlanExecutor`; replace foreach loops; ForceFresh deletes plan file |
| `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` | Read/write plan file for TFS task status |

---

## Complexity Tracking

| Decision | Why Needed | Simpler Alternative Rejected Because |
|----------|-----------|--------------------------------------|
| Kahn's algorithm for tier extraction | Correct topological sort with cycle detection in O(V+E) | Naïve recursion: O(V²), no cycle detection |
| `Task.WhenAll` per tier, not task-level dependency tracking | Tiers are the natural unit of parallel execution; within a tier all tasks are independent | Full task graph with per-task continuations: over-engineered for ≤10 modules |
| `DependsOn` on `JobTask` references task IDs not module names | Task IDs are phase-scoped (`import.identities` ≠ `export.identities`); module names are phase-independent | Store module names: ambiguous — Identities/Export and Identities/Import are different tasks |
| Plan file in `IStateStore` not `IArtefactStore` | Plan is operational state (checkpoint data), not migration output | Store in `IArtefactStore`: violates the state/artefact split; makes plan visible as an output artefact |
| `LoadOrResetAsync` resets `Running → Pending` (not `Failed`) | Running = process crashed mid-task; task may have done partial work; retry with cursor resume | Mark `Running` as `Failed`: causes dependent tasks to be skipped even though the task's module cursor may allow safe retry |
| TfsJobAgentWorker writes plan via `IStateStore` directly (not via `IJobPlanExecutor`) | TFS agent has one module, no tier logic needed; calling a .NET 10 interface pattern in net481 is unnecessary overhead | Use `IJobPlanExecutor` in TFS: overfits; the executor is multi-targeted but the parallel-tier logic adds no value for a single-module agent |
