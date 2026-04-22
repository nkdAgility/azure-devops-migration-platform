Feature: Pre-Import and Post-Import Validation
  As a migration operator
  I want the system to validate the migration package before and after import
  So that corrupted or incomplete packages are rejected before they cause data loss

  Background:
    Given a migration package exists at the configured package root

  Scenario: Validation passes for a well-formed package
    Given the package contains valid revision.json files with all required fields
    And the package schema version matches a supported version
    When the validation pass runs
    Then the validation result is "Passed"
    And no errors are written to ".migration/Logs/"

  Scenario: Validation fails when a revision.json is missing required fields
    Given a revision folder contains a "revision.json" missing the "workItemId" field
    When the validation pass runs
    Then the validation result is "Failed"
    And an error is recorded in ".migration/Logs/" identifying the offending folder and missing field

  Scenario: Validation fails when the package schema version is unsupported
    Given the package "manifest.json" declares schemaVersion "99.0" for the WorkItems module
    When the validation pass runs
    Then the validation result is "Failed"
    And an error is recorded indicating the unsupported schema version

  Scenario: Import does not begin when pre-import validation fails
    Given the pre-import validation pass returns "Failed"
    When the import phase is triggered in Both mode
    Then the import phase does not start
    And the migration job status is set to "ValidationFailed"

  Scenario: Validation runs after import to confirm target state
    Given the import phase has completed successfully
    When the post-import validation runs
    Then each work item in the target is checked against its final revision.json
    And any discrepancy is recorded in ".migration/Logs/post-import-validation.log"

  Scenario: Validation has no side effects on the package
    Given a valid package
    When the platform validates the package
    Then no files in the package are modified
    And no files are created in the package
    And no target API calls are made
