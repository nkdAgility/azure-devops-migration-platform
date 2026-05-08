# agent_task_builder — Task Builder System

- Tag: `agent_task_builder`
- Responsibility: Build ordered `JobTaskList` plans from job kind, enabled modules, and dependency graph; persist `plan.json` for resume.
- Plan shape: `JobTaskList` preserves the flat ordered `Tasks` list used by execution and also exposes ordered phase summaries so consumers can render canonical stage groupings without reconstructing them from task rows.

## Core Classes

- `JobExecutionPlanBuilder`
- `IJobExecutionPlanBuilder`
- `JobTaskList`
- `JobTask`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderDependsOnTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderContextResolutionTests.cs`

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant TB as JobExecutionPlanBuilder
  participant ST as IStateStore
  participant CP as ControlPlane

  JW->>TB: BuildAndSaveAsync(packageConfig, kind)
  TB->>ST: Read plan.json (LoadOrReset)
  alt Existing compatible plan
    TB-->>JW: Return persisted JobTaskList
  else Build fresh plan
    TB->>TB: BuildPlanAsync()
    TB->>ST: Write plan.json
    TB-->>JW: Return fresh JobTaskList
  end
  JW->>CP: POST /agents/lease/{leaseId}/tasks
```
