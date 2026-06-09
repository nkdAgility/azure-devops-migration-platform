# Conversion Summary: job-execution-plan

## Tests Created
File: `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Jobs/JobExecutionPlanDslTests.cs`
Class: `JobExecutionPlanDslTests`

| Scenario | Test Method | Result |
|----------|-------------|--------|
| Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks | `Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks` | PASS |
| Bootstrap_BeforePlanPushed_ReturnNullTasks | `Bootstrap_BeforePlanPushed_ReturnNullTasks` | PASS |
| GetTasks_WhenTaskListExists_ReturnsCurrentTaskList | `GetTasks_WhenTaskListExists_ReturnsCurrentTaskList` | PASS |
| GetTasks_WhenNoTaskListPushed_Returns204 | `GetTasks_WhenNoTaskListPushed_Returns204` | PASS |

All tests carry `[TestCategory("UnitTest")]`.
