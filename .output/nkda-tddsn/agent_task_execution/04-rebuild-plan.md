# 04 — Rebuild Plan: agent_task_execution

## Rebuild Plan

1. Add a failing regression test for import partial resume where a completed dependency must satisfy a pending dependent.
2. Add a failing regression test for generic execution where a pre-skipped dependency must skip the pending dependent without invoking the handler.
3. Implement shared blocked-dependency skip propagation in `JobPlanExecutor`.
4. Apply shared propagation to generic task execution, export/prerequisite execution, and import execution.
5. Ensure runnable task filters exclude terminal failed/skipped/completed states.
6. Update architecture context to record resume dependency semantics.
7. Run the focused infrastructure agent test project using PowerShell.
8. Produce implementation and verification artefacts.

## Minimal Production Change Gate

### Required production change

`JobPlanExecutor` needs shared blocked-dependency propagation before tier extraction.

### Behaviour being corrected

- A pending dependent should run when the dependency is already `Completed`.
- A pending dependent should skip when the dependency is already `Skipped` or `Failed`.

### Why this is minimal

The change is limited to executor state filtering and skip propagation. It does not alter plan building, task contracts, module APIs, control-plane APIs, connector code, or job dispatch.

### Architecture documentation impact

The subsystem architecture context required a concise update to document the explicit resume semantics.
