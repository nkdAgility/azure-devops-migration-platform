# TDD Safety Net Rebuild Plan: agent_package_persistence

## Priority 1: Stop Critical Drift

- Add `PackageProgressSink_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder` to expose progress records drifting to fallback logs after state clear.
- Add `PackageLoggerProvider_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder` to expose diagnostic records drifting to fallback logs after state clear.

## Priority 2: Replace Weak Verification Tests

- Keep existing `PackageLoggerProviderRotationTests`; add focused run-folder flush tests instead of rewriting rotation tests.

## Priority 3: Add Boundary Protection

- Assert only the observable `IArtefactStore.AppendAsync` path contract.
- Avoid background timing dependencies by using explicit `FlushAsync`.

## Priority 4: Improve Design Pressure

- Capture the active log folder whenever a sink captures an active store.
- Select the cached log folder when flushing through the cached store after state clear.

## Priority 5: Consolidate and Clean Up

- Place progress and logger run-folder tests in one telemetry-focused test class.
- Leave broader architecture unchanged.

## Safe Stopping Points

- After adding failing tests, no production behaviour should be changed until the run-folder drift is confirmed.
- After caching log folders in both sinks, run the focused telemetry tests.

## Production Code Seams Required

- Add a private cached log-folder field to each package sink. No public API changes are required.
