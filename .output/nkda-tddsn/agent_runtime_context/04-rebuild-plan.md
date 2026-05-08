# TDD Safety Net Rebuild Plan: agent_runtime_context

## Priority 1: Stop Critical Drift

- Fix `AgentJobContext` package-path validation so Windows drive-rooted paths, UNC paths, and Unix rooted paths are accepted consistently across host OSes.
- Preserve rejection of empty, whitespace, and relative package paths.

## Priority 2: Replace Weak Verification Tests

- Replace the single valid `Dependencies` mode test with a data-driven valid-mode test covering `Dependencies`, `Export`, `Import`, `Prepare`, and `Migrate` while keeping existing `Inventory` coverage.

## Priority 3: Add Boundary Protection

- Add direct tests for `CurrentPackageConfigAccessor` set/clear/null semantics.
- Add direct tests for `CurrentAgentJobContextAccessor` set/clear/null semantics.
- Add direct tests for `CurrentJobEndpointAccessor` independent source/target clearing, full clear, and null rejection.

## Priority 4: Improve Design Pressure

- Keep accessor tests outcome-based and avoid asserting private fields or implementation details.
- Keep path validation tests expressed through successful/failed context construction rather than a private helper.

## Priority 5: Consolidate and Clean Up

- Keep existing `PackageConfigStoreTests.cs` unchanged.
- Leave worker dispatch test compilation as explicit follow-up rather than broadening this subsystem pass.

## Safe Stopping Points

- After valid-mode tests are updated and the package-path production fix is in place.
- After accessor tests are added and pass locally.
- After documentation and workflow artefacts are written.

## Production Code Seams Required

- No new seam required.
- Minimal production change: replace host-dependent `Path.IsPathRooted` with host-independent package-path absolute validation inside `AgentJobContext`.
