# DSL Design: runtime-state-cadence-US3-fine-grained-progress-save-cadence

## Target Test Class
`WorkItemBatchResumeCadenceTests` in
`tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/WorkItems/WorkItemBatchResumeCadenceTests.cs`

## New Methods Added

### ReplayCoverageRatio_RemainsWithinThresholdAfterResume
- Asserts `ReplayCoverageRatio(100, 50) >= 0.5`
- Covers: "replay after resume remains within the defined replay threshold"

### ShouldPersist_SteadyForwardMovement_AfterResume
- Simulates 3 sequential batches of 50 items, each triggering a persist
- Covers: "progress output continues with steady forward movement"

## Categories
All methods tagged `[TestCategory("UnitTest")]`.
