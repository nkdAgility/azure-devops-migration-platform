Feature: Work item field exclusion and clearing
  As a migration operator
  I want to exclude or clear unwanted fields
  So that sensitive or irrelevant fields are not migrated to the target system

  Scenario: Exclude transform removes the field entirely
    Given a work item with field "Custom.InternalOnly" set to "secret"
    And an ExcludeField transform is configured for field "Custom.InternalOnly"
    When the field transform pipeline executes
    Then the field "Custom.InternalOnly" should not be present in the output

  Scenario: Clear transform sets the field value to null
    Given a work item with field "Custom.Notes" set to "some notes"
    And a ClearField transform is configured for field "Custom.Notes"
    When the field transform pipeline executes
    Then the field "Custom.Notes" should have value null

  Scenario: Exclude on absent field succeeds silently
    Given a work item without field "Custom.Missing"
    And an ExcludeField transform is configured for field "Custom.Missing"
    When the field transform pipeline executes
    Then the pipeline should complete without error
