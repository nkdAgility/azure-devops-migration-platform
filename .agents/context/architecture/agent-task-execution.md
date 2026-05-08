# agent_task_execution — Task Execution System

- Tag: `agent_task_execution`
- Responsibility: Execute plan tiers, enforce `DependsOn`, transition task states, persist status transitions, and emit task progress.

## Core Classes

- `JobPlanExecutor`
- `IJobPlanExecutor`
- `JobTaskStatus`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PlanDrivenExecutionSteps.cs`

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant TE as JobPlanExecutor
  participant MOD as IModule
  participant ST as IStateStore
  participant PS as IProgressSink

  JW->>TE: ExecuteExportPhaseAsync / ExecuteImportPhaseAsync
  TE->>TE: Extract tiers from JobTaskList
  loop Each tier
    TE->>MOD: Run task handler(s)
    MOD-->>TE: Success/Failure
    TE->>ST: Persist updated plan/task state
    TE->>PS: Emit ProgressEvent(task status)
  end
  TE-->>JW: bool success
```
