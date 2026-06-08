# DSL Design: runtime-state-identity-US2-action-qualified-cursors

## Design Decision
No new DSL surface required. The scenario maps directly to unit tests on `StateCursorIdentity` using the existing MSTest pattern.

## Test Classes
- `ActionQualifiedCursorIdentityTests` — verifies different actions produce different keys
- `StateCursorIdentityTests` — verifies key format (action.module) and parse round-trip

## Mapping
| Scenario assertion | Test method |
|---|---|
| cursor path includes action and module identity | `StateCursorIdentityTests.Build_ReturnsLowercaseActionQualifiedIdentity` |
| no phase overwrites another phase cursor | `ActionQualifiedCursorIdentityTests.Build_WithDifferentActions_ProducesDifferentKeys` |
| TryParse round-trip | `StateCursorIdentityTests.TryParse_ActionQualifiedValue_ReturnsActionAndModule` |
