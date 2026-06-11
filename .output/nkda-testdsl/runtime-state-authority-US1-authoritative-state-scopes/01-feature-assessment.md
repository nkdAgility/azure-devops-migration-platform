# Feature Assessment: runtime-state-authority-US1-authoritative-state-scopes

## Feature File
Path: `features/platform/runtime-state-authority/US1-authoritative-state-scopes.feature`
Status: Not present in `small-fixes` branch (exists only in worktree `crazy-goldberg-c58e96`)

## Scenarios
1. `Resume_UsesAuthoritativeScopes_RunScopeIgnored` — tests that resume and phase-gate use only root/project scoped state; run-scoped audit copies are not used as authoritative state.

## Wiring State
Unwired — no Reqnroll step bindings found in tests/ on this branch.

## Key Types
- `RunScopeAuthorityGuard` (src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/)
- `PackagePathTestHelper` (tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/TestUtilities/)

## Coverage Assessment
Partial pre-existing coverage in:
- `RunAuditInspectabilityTests.RunAuditPath_IsInspectable_ButNotAuthoritative` — tests single path
- `RunScopeAuthorityGuardTests` — unit tests for the guard itself

Missing: composite scenario asserting authoritative root/project paths pass guard AND run-scope stale copies are rejected.
