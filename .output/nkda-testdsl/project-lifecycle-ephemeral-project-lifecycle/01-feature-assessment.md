# Feature Assessment: ephemeral-project-lifecycle

**Family**: project-lifecycle-ephemeral-project-lifecycle
**Feature file**: features/platform/project-lifecycle/ephemeral-project-lifecycle.feature

## Scenarios

| Tag | Title | Steps |
|-----|-------|-------|
| @US1 | Eligible run creates and tears down project successfully | Given/When/When/Then/Then |
| @US2 | Teardown is attempted when test execution fails | Given/And/When/Then |
| @US3 | Eligibility respects connector type (outline x2) | Given/Then for AzureDevOpsServices, TeamFoundationServer |

## Source types involved

- `ProjectLifecycleService` (DevOpsMigrationPlatform.Infrastructure.Agent)
- `SimulatedProjectLifecycleProvider` (DevOpsMigrationPlatform.Infrastructure.Simulated)
- `LifecycleEligibilityFlag` (DevOpsMigrationPlatform.Abstractions.Agent)
- `ProjectLifecycleContext`, `ProjectLifecycleRecord` (abstractions)

## Wiring state

Wired — step bindings exist in `ProjectLifecycleSteps.cs` + `ProjectLifecycleScenarioContext.cs`, with the feature file referenced via `ExternalFeatureFiles` in the .csproj.

## Risk assessment

Low. All scenarios map directly to existing infrastructure-layer logic that already has partial unit-test coverage in `ProjectLifecycleServiceTests` and `AzureDevOpsProjectLifecycleServiceTests`.
