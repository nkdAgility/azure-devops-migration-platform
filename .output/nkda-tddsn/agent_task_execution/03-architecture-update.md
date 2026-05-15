# 03 — Architecture Update: agent_task_execution

## Architecture Refresh

The architecture narrative for `agent_task_execution` now needs to distinguish dependency satisfaction from dependency blocking during resume:

- `Completed` dependencies are satisfied and must allow pending dependents to execute.
- `Skipped` and `Failed` dependencies are blockers and must cascade `Skipped` to pending dependents before tier extraction.
- The skip cascade must apply consistently across generic task execution, export/prerequisite execution, and import execution.
- Persisted skipped dependent state is part of the package/control-plane contract so UI bootstrap and resumed agents see the same task state.

## Documentation Change Applied

Updated `.agents/30-context/architecture/agent-task-execution.md` with explicit dependency and resume semantics.

## Guardrail Alignment

- Preserves package state as the durable source of truth.
- Preserves control-plane-visible task state through `JobTaskList`.
- Does not introduce UI coupling or direct filesystem access.
- Does not change connector behaviour.

