Feature: Work item field copying and renaming
  As a migration operator
  I want to copy or rename work item fields
  So that field data is preserved under new field names in the target system

  Scenario: Field value is copied from source to target field
    Given a work item with field "Custom.OldField" set to "some value"
    And a CopyField transform is configured to copy from "Custom.OldField" to "Custom.NewField"
    When the field transform pipeline executes
    Then the field "Custom.NewField" should have value "some value"

  Scenario: Default value is used when source field is absent
    Given a work item without field "Custom.OldField"
    And a CopyField transform is configured to copy from "Custom.OldField" to "Custom.NewField" with default "N/A"
    When the field transform pipeline executes
    Then the field "Custom.NewField" should have value "N/A"

  Scenario: Empty value is copied rather than using default
    Given a work item with field "Custom.OldField" set to ""
    And a CopyField transform is configured to copy from "Custom.OldField" to "Custom.NewField" with default "N/A"
    When the field transform pipeline executes
    Then the field "Custom.NewField" should have value ""

  Scenario: Target field is overwritten unconditionally
    Given a work item with field "Custom.Source" set to "new value" and "Custom.Target" set to "old value"
    And a CopyField transform is configured to copy from "Custom.Source" to "Custom.Target"
    When the field transform pipeline executes
    Then the field "Custom.Target" should have value "new value"
