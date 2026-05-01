@cli
Feature: Config File Validated Against Schema Before Queue Submission
  Before job submission, validate raw migration.json against schema to catch unknown keys and structural errors

  Background:
    Given the CLI has a deployed migration.schema.json

  @tier-0-validation
  Scenario: ValidConfig_SchemaPresent_PassesSilently
    Given a migration.json that is fully valid against the schema
    When the operator runs devopsmigration queue
    Then schema validation passes without logging an error
    And the command proceeds to submit the job

  @tier-0-validation
  Scenario: UnknownKey_SchemaPresent_ExitsNonZero
    Given a migration.json with an unknown key "unknownField" at the top level
    When the operator runs devopsmigration queue
    Then the CLI exits with a non-zero code
    And an error is logged with the JSON path "unknownField"
    And the error includes the constraint "additionalProperties"
    And no job is submitted to the control plane

  @tier-0-validation
  Scenario: MissingRequiredField_SchemaPresent_ExitsNonZero
    Given a migration.json with source.type absent
    When the operator runs devopsmigration queue
    Then the CLI exits with a non-zero code
    And an error is logged with the JSON path "source.type"
    And no job is submitted to the control plane

  @tier-0-validation
  Scenario: SchemaAbsent_ValidConfig_LogsWarningAndProceeds
    Given migration.schema.json is absent from the CLI output directory
    And a migration.json that would be valid if the schema were present
    When the operator runs devopsmigration queue
    Then a warning is logged identifying the expected schema path
    And the command proceeds to submit the job
