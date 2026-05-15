# 01 — Assessment: agent_task_execution

## Scope

Subsystem: `agent_task_execution`.

Primary context:

- `.agents/30-context/architecture/agent-task-execution.md`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderDependsOnTests.cs`

## Behaviour Model Gate

### Subsystem purpose

`agent_task_execution` executes a persisted `JobTaskList` for migration agents. It must preserve deterministic phase ordering, enforce `DependsOn`, transition task state through the task lifecycle, persist every status transition, and emit task-level progress events for control-plane and UI consumers.

### Primary behaviours

1. Build or consume a `JobTaskList` with stable task IDs, task kind, phase, order, and dependency metadata.
2. Extract runnable task tiers from dependency edges.
3. Execute independent tasks in the same tier concurrently.
4. Prevent dependent tasks from running when an upstream dependency is skipped or failed.
5. Treat completed dependencies as satisfied when resuming a partially completed plan.
6. Persist transitions to `PackagePaths.PlanFile` after running/completed/failed/skipped updates.
7. Emit task lifecycle progress events with task ID, status, and progress counts when available.
8. Reset `Running` tasks to `Pending` when loading a persisted plan after a crash.

### State transitions

- `Pending` → `Running` → `Completed`
- `Pending` → `Running` → `Failed`
- `Pending` → `Skipped` when a dependency is skipped/failed or prerequisite state makes the task unnecessary
- `Running` → `Pending` during crash recovery via `LoadOrResetAsync`

### External contracts

- `JobTaskList`/`JobTask` are the control-plane-visible task contract.
- `IStateStore` persists package job plan state.
- `IProgressSink` emits lifecycle updates.
- `IModule`, `ICapture`, and `IAnalyser` handlers are dispatched by task kind.

### Failure and rejection behaviours

- Missing handler registration fails the task and returns a failed execution result without cancelling sibling tasks in the same tier.
- Failed upstream dependency skips downstream tasks.
- Corrupt persisted plan returns `null` from `LoadOrResetAsync` so the caller can rebuild.
- Unknown capture organisation URL fails the capture task without invoking the handler.

### Boundary conditions

- Completed or skipped tasks are not re-executed on resume.
- Completed dependency tasks must not block their dependents.
- Skipped/failed dependency tasks must cascade to direct and transitive dependents.
- Capture tasks may be organisation/project-scoped.
- Progress totals may be absent and must remain nullable.

## Current Test Inventory

| Test class | Current protection |
| --- | --- |
| `JobExecutionPlanBuilderTests` | Plan shape, ordering, disabled modules, persisted plan reuse, mode mismatch reset |
| `JobExecutionPlanBuilderDependsOnTests` | Dependency graph generation, circular dependency rejection, disabled/missing dependency handling, inventory/dependency capture plans |
| `JobPlanExecutorTests` | Concurrency, prerequisites, dependency wait/skip, failed sibling isolation, resume, capture routing, progress snapshots |
| `JobAgentWorkerDispatchTests` | Agent dispatch into unified task execution and marker handling |
| `JobAgentWorkerInventoryTests` | Inventory dispatch for multiple source endpoints |

## TDD Quality Assessment

| Area | Score | Notes |
| --- | ---: | --- |
| Behaviour focus | 4/5 | Tests generally assert observable task execution, persistence, and progress behaviour. |
| Drift prevention | 3/5 | Good graph/build coverage, but resume dependency semantics had a gap. |
| Refactor safety | 4/5 | Most tests use public executor/builder contracts rather than private methods. |
| Boundary coverage | 3/5 | Existing coverage included failed dependencies but not completed-dependency resume and pre-skipped generic dependencies. |
| Over-mocking risk | 3/5 | Module mocks are necessary but some tests assert call counts rather than persisted plan state. |

## Drift Risks Identified

1. A completed dependency on a resumed import plan could be treated as a blocker rather than a satisfied dependency.
2. A pre-skipped dependency in generic task execution could be omitted from runnable tier extraction and accidentally treated as satisfied.
3. Failed persisted tasks could be selected for rerun unless terminal states are consistently filtered.
4. Architecture documentation did not explicitly state resume semantics for completed versus skipped/failed dependencies.

## Assessment Conclusion

The safety net was broadly useful but incomplete for dependency resume semantics. The target rebuild should add behavioural regression tests for completed-dependency resume and pre-skipped dependency cascade, then minimally adjust executor filtering and skip propagation.

