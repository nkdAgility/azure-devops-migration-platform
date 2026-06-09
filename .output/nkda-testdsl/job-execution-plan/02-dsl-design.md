# DSL Design: job-execution-plan

## Target Test Class
`DevOpsMigrationPlatform.ControlPlane.Tests.Jobs.JobExecutionPlanDslTests`

## File
`tests/DevOpsMigrationPlatform.ControlPlane.Tests/Jobs/JobExecutionPlanDslTests.cs`

## Context Helper
`BuildController(InMemoryJobTaskStore, Mock<ILeaseJobResolver>?)` — constructs a `TelemetryController` with in-memory stores.

## Test Methods
| Scenario | Method |
|----------|--------|
| Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks | `Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks` |
| Bootstrap_BeforePlanPushed_ReturnNullTasks | `Bootstrap_BeforePlanPushed_ReturnNullTasks` |
| GetTasks_WhenTaskListExists_ReturnsCurrentTaskList | `GetTasks_WhenTaskListExists_ReturnsCurrentTaskList` |
| GetTasks_WhenNoTaskListPushed_Returns204 | `GetTasks_WhenNoTaskListPushed_Returns204` |

## Approach
Direct unit test of `TelemetryController` — no HTTP test server needed. Uses `InMemoryJobTaskStore` directly to verify bootstrap/tasks contract.
