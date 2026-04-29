@us3-legacy-fail-fast
Feature: Agent fails fast when migration-config.json is absent
  As a migration operator
  I want the Migration Agent to fail immediately when migration-config.json is missing from the package
  So that I receive a clear error rather than a silent or misleading failure

  Background:
    Given a job has been submitted to the queue
    And the package for the job does NOT contain migration-config.json

  Scenario: Agent marks job failed when config file is absent
    When the Migration Agent picks up the job
    Then the job is marked as failed with reason "Config file not found"
    And no modules are executed

  Scenario: Failure log message includes the package URI
    When the Migration Agent picks up the job
    Then a structured error log entry is emitted containing the package URI

  Scenario: Agent does not retry when config file is absent
    When the Migration Agent picks up the job
    Then the job transitions directly to the terminal "fail" state without retrying

  Scenario: Resubmitting the job after adding config succeeds
    Given the operator writes a valid migration-config.json to the package
    And the job is resubmitted via the CLI
    When the Migration Agent picks up the resubmitted job
    Then the job proceeds to module execution
