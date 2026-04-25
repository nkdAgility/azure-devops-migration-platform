Feature: Work item field merging and conditional assignment
  As a migration operator
  I want to merge multiple fields into one or conditionally set field values
  So that composite field content is preserved and field values can be derived from conditions

  Scenario: Both source fields present and merge succeeds
    Given a work item with field "Custom.FirstName" set to "John" and "Custom.LastName" set to "Doe"
    And a MergeFields transform is configured for field "Custom.FullName" with source fields "Custom.FirstName,Custom.LastName" and format "{0} — {1}"
    When the field transform pipeline executes
    Then the field "Custom.FullName" should have value "John — Doe"

  Scenario: Absent source field is treated as empty string
    Given a work item with field "Custom.FirstName" set to "John" but without field "Custom.LastName"
    And a MergeFields transform is configured for field "Custom.FullName" with source fields "Custom.FirstName,Custom.LastName" and format "{0} {1}"
    When the field transform pipeline executes
    Then the field "Custom.FullName" should have value "John "

  Scenario: ConditionalField sets trueValue when condition matches
    Given a work item with field "System.State" set to "Active"
    And a ConditionalField transform is configured to set "Custom.IsActive" to "Yes" when "System.State" matches "Active" else "No"
    When the field transform pipeline executes
    Then the field "Custom.IsActive" should have value "Yes"

  Scenario: ConditionalField sets falseValue when condition does not match
    Given a work item with field "System.State" set to "Closed"
    And a ConditionalField transform is configured to set "Custom.IsActive" to "Yes" when "System.State" matches "Active" else "No"
    When the field transform pipeline executes
    Then the field "Custom.IsActive" should have value "No"
