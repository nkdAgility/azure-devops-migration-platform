@us1-config-write
Feature: Config written to package before job submission
  As a migration operator
  I want the CLI to write migration-config.json to the package before submitting a job
  So that the Migration Agent can read tool configuration without needing the original config file

  Background:
    Given a valid migration.json configuration file with field-transform and node-structure tools enabled
    And an output package directory exists

  @us1-write-idempotency
  Scenario: CLI fails with a clear error when migration-config.json already exists
    Given migration-config.json already exists in the package
    When the operator runs "migrate queue export --output <packagePath>"
    Then the command exits with a non-zero status code
    And the error message references "already exists"
    And no duplicate job submission is made
