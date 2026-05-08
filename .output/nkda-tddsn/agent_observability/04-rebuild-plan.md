# TDD Safety Net Rebuild Plan: agent_observability

## Priority 1: Stop Critical Drift

- Add DiagnosticLogStoreTests.Add_WhenRecordIsBelowDeploymentMinimumLevel_DiscardsRecordBeforeBuffering to prevent low-severity diagnostics from leaking into retained buffers.
- Add DiagnosticLogStoreTests.Subscribe_WhenRecordIsAdded_NotifiesLiveSubscriberWithoutPollingSnapshot to protect live stream delivery.
- Add DiagnosticLogStoreTests.Subscribe_WhenJobAlreadyCompleted_CompletesSubscriberImmediately to prevent late-client hangs after terminal jobs.

## Priority 2: Replace Weak Verification Tests

- Do not replace existing weak ControlPlaneProgressSink structural assertions in this pass; keep them and record as follow-up because the approved target suite is limited to DiagnosticLogStore.

## Priority 3: Add Boundary Protection

- Add DiagnosticLogStoreTests.Add_WhenRingBufferExceedsCapacity_EvictsOldestRetainedRecord for retention boundary behaviour.
- Add DiagnosticLogStoreTests.GetSnapshot_WhenLevelFilterIsProvided_ReturnsRecordsAtOrAboveRequestedLevel for client filter boundary behaviour.

## Priority 4: Improve Design Pressure

- Keep tests directly against the public DiagnosticLogStore API so design pressure stays on explicit store contracts, not private queue/subscriber implementation.
- Use real DiagnosticLogRecord values and Options.Create rather than mocks.

## Priority 5: Consolidate and Clean Up

- Place the tests in tests/DevOpsMigrationPlatform.ControlPlane.Tests/Diagnostics/DiagnosticLogStoreTests.cs to keep Control Plane diagnostics tests separate from progress tests.
- Do not delete or rename current feature files or step definitions in this pass.

## Safe Stopping Points

- After adding the test class and before production changes: run the focused ControlPlane.Tests filter.
- If all target tests pass without production changes: stop and record that no code adjustment was needed.
- If any target test fails due to documented behaviour mismatch: make the smallest production change in DiagnosticLogStore only, then rerun the focused test command.

## Production Code Seams Required

- None. DiagnosticLogStoreOptions already supplies deterministic configuration, and ChannelReader/ChannelWriter are public return values for subscriber verification.
