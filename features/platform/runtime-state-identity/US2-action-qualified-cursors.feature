@platform
Feature: Action-qualified cursor identity
  Ensure inventory, export, and import cursor namespaces are isolated by action.

  @runtime-state-us2
  Scenario: CursorIdentity_IsolatedByAction_NoCollisions
    Given inventory export and import run for the same module and project
    When each phase updates its checkpoint cursor
    Then each cursor path includes both action and module identity
    And no phase overwrites another phase cursor
