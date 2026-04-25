Feature: Work item field regex cleanup
  As a migration operator
  I want to apply regex find-and-replace to field values
  So that field content can be cleaned up or normalised during migration

  Scenario: Pattern matches and replaces content
    Given a work item with field "System.Title" set to "[OLD] Implement login page"
    And a RegexField transform is configured for field "System.Title" with pattern "\[OLD\] " and replacement ""
    When the field transform pipeline executes
    Then the field "System.Title" should have value "Implement login page"

  Scenario: Pattern does not match and field is left unchanged
    Given a work item with field "System.Title" set to "Implement login page"
    And a RegexField transform is configured for field "System.Title" with pattern "\[OLD\] " and replacement ""
    When the field transform pipeline executes
    Then the field "System.Title" should have value "Implement login page"
