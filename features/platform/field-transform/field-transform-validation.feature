Feature: Work item field transform configuration validation
  As a migration operator
  I want the migration platform to validate field transform configuration before migration starts
  So that configuration errors are caught early before any data is processed

  Background:
    Given field transform configuration has been loaded

  Scenario: Valid configuration passes validation
    Given the transform configuration references only existing fields
    When the field transform validator runs
    Then the validation report should indicate success

  Scenario: Invalid field reference is detected
    Given the transform configuration references field "Custom.NonExistent" that does not exist in the source
    When the field transform validator runs
    Then the validation report should contain an error for field "Custom.NonExistent"

  Scenario: Field type mismatch is detected
    Given the transform configuration maps a text field to a numeric field
    When the field transform validator runs
    Then the validation report should contain a warning about type incompatibility

  Scenario: Sample dry-run executes against configured items
    Given the transform configuration is valid
    And the source system has at least one work item available
    When the field transform validator runs with sample size 5
    Then the validation report should confirm the dry-run completed
