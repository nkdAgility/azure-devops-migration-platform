# 02 — Target Test Suite: agent_task_execution

## Target Suite Gate

| Decision | Test class | Test method | Type | Protected behaviour | Expected assertions |
| --- | --- | --- | --- | --- | --- |
| Keep | `JobExecutionPlanBuilderTests` | Existing plan shape and persisted-plan tests | Unit/behaviour | Builder emits deterministic task plans and reuses terminal persisted plans | Task counts/order/statuses match contract |
| Keep | `JobExecutionPlanBuilderDependsOnTests` | Existing dependency graph tests | Unit/behaviour | Builder creates valid dependency edges and rejects cycles | `DependsOn` arrays and exceptions match expected graph |
| Keep | `JobPlanExecutorTests` | `ExecuteImportPhaseAsync_WorkItemsDependsOnIdentities_WaitsForIdentities` | Unit/behaviour | Dependent import task waits for upstream completion | Execution order has identities before workitems |
| Keep | `JobPlanExecutorTests` | `ExecuteImportPhaseAsync_IdentitiesFails_WorkItemsSkipped` | Unit/behaviour | Failed upstream dependency skips dependent import task | Result fails and dependent handler is not invoked |
| Add | `JobPlanExecutorTests` | `ExecuteImportPhaseAsync_PartialResume_CompletedDependencyAllowsDependentTaskToRun` | Unit/behaviour | Completed dependency satisfies a pending dependent during resume | Result succeeds and pending dependent handler runs |
| Add | `JobPlanExecutorTests` | `ExecuteTasksAsync_SkippedDependency_SkipsDependentTaskWithoutInvokingHandler` | Unit/behaviour | Generic execution cascades a pre-skipped dependency to dependent tasks | Result succeeds as no runnable task fails, dependent handler is not invoked, skipped state persists |
| Keep | `JobPlanExecutorTests` | `LoadOrResetAsync_RunningTasksAreResetToPending` | Unit/behaviour | Crash recovery clears transient running state | Running tasks become pending |
| Keep | `JobAgentWorkerDispatchTests` | Existing dispatch tests | Unit/behaviour | Worker dispatches job kinds into task execution and marker writing | Executor receives expected route and markers are written only on success |

## Proposed Minimal Skeletons

```csharp
[TestMethod]
public async Task ExecuteImportPhaseAsync_PartialResume_CompletedDependencyAllowsDependentTaskToRun()
{
    // Arrange plan: import.identities completed, import.workitems pending depends on identities.
    // Act import execution.
    // Assert workitems executes and result succeeds.
}

[TestMethod]
public async Task ExecuteTasksAsync_SkippedDependency_SkipsDependentTaskWithoutInvokingHandler()
{
    // Arrange plan: capture.identities skipped, capture.workitems pending depends on identities.
    // Act generic task execution.
    // Assert workitems handler is not invoked and skipped state is persisted.
}
```

## Test Decisions for Relevant Existing Tests

- Keep dependency wait, failure, and sibling isolation tests because they encode essential execution semantics.
- Keep partial export resume test because it protects non-reexecution of completed tasks.
- Add import partial resume with dependency because the existing import all-completed test does not cover a pending dependent with completed dependency.
- Add generic skipped dependency cascade because unified inventory/dependency execution uses `ExecuteTasksAsync`, not the import-specific path.
