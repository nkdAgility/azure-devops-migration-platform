# ADR 0010 — Plan-Driven DAG Execution

## Status

Accepted — amended by iron-comms unification (2026-07-01): task-list push transport changed, see Amendment below and [ADR-0020](0020-unified-worker-event-channel.md)

## Context

The agent previously executed modules in a hardcoded `foreach (var module in jobModules)` loop. The loop happened to produce the correct order only because DI registration order matched the intended execution order — an accidental invariant. The loop had four structural problems:

1. **No dependency enforcement.** `IModule.DependsOn` was documented but not acted upon. A configuration change or future module addition could silently run WorkItems import before Identities import, producing corrupt target state.
2. **No parallelism.** Export modules read from the source system independently — there is no data dependency between them. The sequential loop introduced unnecessary latency.
3. **No task-level resume.** Cursor-based checkpointing (ADR-0003) handles item-level resume within a module. If the agent crashed mid-job, the new agent had no record of which modules had already completed and re-executed them all.
4. **No visibility before execution.** Operators connecting to a running job had no structural view of what the agent was about to do.

## Decision

The Agent builds an execution plan from `IModule.DependsOn` declarations, persists it, and drives all execution from the plan.

**Plan build:**

1. At job start, `IJobExecutionPlanBuilder.BuildPlanAsync` reads the enabled modules and their `DependsOn` graphs.
2. Modules with no remaining dependencies form an execution tier and run concurrently.
3. When all tasks in a tier complete, the next tier is evaluated. Tiers are processed sequentially; tasks within a tier are processed concurrently.
4. If a task fails, tasks in subsequent tiers that depend on the failed task are marked `Skipped` with a `skipReason` referencing the failed dependency. Tasks in the same tier with no dependency on the failed task continue.
5. A circular dependency graph causes an `InvalidOperationException` before any module executes.

**Plan persistence:**

- The plan is persisted to `.migration/Checkpoints/plan.json` via `IStateStore` immediately after build, before the first module executes.
- The plan is pushed to the Control Plane so clients can display the full task list before any work begins. _(Originally via `IControlPlaneTelemetryClient.PushTaskListAsync`; see Amendment.)_
- On resume, if `plan.json` exists in the package, the plan is reloaded rather than rebuilt. Tasks with `status: Running` (crashed mid-way) are reset to `Pending`. Tasks with `status: Completed` are not re-executed. `ForceFresh` deletes `plan.json` and rebuilds.

**Relationship to ADR-0003:** Cursor-based checkpointing operates at the item level within a single module. Plan-level checkpointing operates at the task level across modules. Both coexist: the plan skips completed-module re-execution; the cursor skips already-processed items within a resumed module.

## Alternatives Considered

**Static ordering by DI registration convention**: What the platform had before. Brittle — any change to DI setup order silently changes execution order.

**Fully sequential, dependency-ignorant execution**: Safe but slow. Does not satisfy parallelism or dependency enforcement requirements.

**External workflow engine (Temporal, Durable Functions)**: Provides DAG execution, persistence, and parallelism but adds an infrastructure dependency. The package-based plan achieves the same outcomes without external services.

## Consequences

- `IModule.DependsOn` is now authoritative — the plan executor enforces it, not registration order.
- Independent modules (e.g., Identities, Nodes, Teams on export) run concurrently by default.
- `WorkItems` import cannot start until both `Identities` and `Nodes` import complete.
- Operators see the full task list with statuses in the TUI/CLI before any module executes (fed by `GET /jobs/{id}/bootstrap`).
- A crash followed by a restart re-uses the persisted plan — previously completed tasks are skipped, not re-run.
- Circular dependency declarations are a job-start failure, not a runtime failure partway through.

## Amendment — Iron-Comms Unification (2026-07-01)

`IControlPlaneTelemetryClient` / `PushTaskListAsync` and the `POST /agents/lease/{leaseId}/tasks` endpoint have been removed. Task-list push now flows through `UnifiedWorkerEventWriter.EnqueueTasks` as a `Tasks`-kind event in the batched `POST /workers/{workerId}/events` channel (both the net10 `JobAgentWorker` and net481 `TfsJobAgentWorker`). The decision itself — plan-driven DAG execution with the plan pushed before any work begins — is unchanged.

## Related

- [ADR-0003](0003-cursor-based-checkpointing.md) — item-level resume (complementary to plan-level)
- [docs/architecture.md](../architecture.md) — agent execution model
- [.agents/30-context/domains/job-lifecycle.md](../../.agents/30-context/domains/job-lifecycle.md) — job lifecycle
- Driving specs: `specs/028.1-task-bootstrap/spec.md`, `specs/028.2-job-execution-by-task/spec.md`

