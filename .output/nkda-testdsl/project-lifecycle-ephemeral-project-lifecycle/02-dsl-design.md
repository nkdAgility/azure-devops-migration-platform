# DSL Design: ephemeral-project-lifecycle

## Approach

Direct MSTest unit tests against `ProjectLifecycleService` and `LifecycleEligibilityFlag` with no shared DSL wrapper needed — the domain surface is simple and self-describing.

## Test class

`ProjectLifecycleServiceTests` in `DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/CompositeProjectLifecycleServiceTests.cs`

## Methods added

| Scenario | Method |
|----------|--------|
| US1 | `EphemeralLifecycle_SimulatedConnector_CreateAndTeardownBothSucceed` |
| US2 | `EphemeralLifecycle_TeardownIsAttemptedWhenTestExecutionFails` |
| US3 row 1 | `EphemeralLifecycle_EligibilityRespects_AzureDevOpsServicesConnector` |
| US3 row 2 | `EphemeralLifecycle_EligibilityRespects_TeamFoundationServerConnector` |

All methods annotated with `[TestCategory("UnitTest")]`.
