Feature: Work item field value remapping
  As a migration operator
  I want to remap field values between process templates
  So that work items have correct state values in the target system

  Background:
    Given a MapValue transform is configured for field "System.State" with mappings "Active" -> "In Progress" and "Resolved" -> "Done"

  Scenario: Mapped value is replaced with the target value
    Given a work item of type "Bug" with field "System.State" set to "Active"
    When the field transform pipeline executes
    Then the field "System.State" should have value "In Progress"

  Scenario: Unmapped value is preserved with a warning
    Given a work item of type "Bug" with field "System.State" set to "New"
    When the field transform pipeline executes
    Then the field "System.State" should still have value "New"

  Scenario: Work item type filter skips non-matching type
    Given a MapValue transform is configured for field "System.State" with mappings "Active" -> "In Progress" and applyTo filter "Bug"
    And a work item of type "Task" with field "System.State" set to "Active"
    When the field transform pipeline executes
    Then the field "System.State" should still have value "Active"

  Scenario: Sequential transforms execute in declaration order
    Given two MapValue transforms: first maps "Active" -> "Intermediate", second maps "Intermediate" -> "Done" for field "System.State"
    And a work item of type "Bug" with field "System.State" set to "Active"
    When the field transform pipeline executes
    Then the field "System.State" should have value "Done"
