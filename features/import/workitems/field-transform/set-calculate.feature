Feature: Work item field value assignment and calculation
  As a migration operator
  I want to set literal values or compute field values from expressions
  So that migrated work items can be stamped with migration metadata or derived values

  Scenario: Literal value is stamped on a field
    Given a work item of type "Bug"
    And a SetField transform is configured for field "Custom.MigratedBy" with value "migration-platform"
    When the field transform pipeline executes
    Then the field "Custom.MigratedBy" should have value "migration-platform"

  Scenario: Computed value is derived from an expression
    Given a work item with field "Custom.Hours" set to "8" and "Custom.Days" set to "5"
    And a CalculateField transform is configured for field "Custom.TotalHours" with expression "Custom.Hours * Custom.Days"
    When the field transform pipeline executes
    Then the field "Custom.TotalHours" should have value "40"

  Scenario: Missing field reference in expression produces error action
    Given a work item without field "Custom.Missing"
    And a CalculateField transform is configured for field "Custom.Result" with expression "Custom.Missing * 2"
    When the field transform pipeline executes
    Then the field "Custom.Result" should not be modified
