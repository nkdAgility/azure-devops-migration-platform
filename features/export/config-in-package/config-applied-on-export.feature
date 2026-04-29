@us1-config-write
Feature: Config written to package before job submission
  As a migration operator
  I want the CLI to write migration-config.json to the package before submitting a job
  So that the Migration Agent can read tool configuration without needing the original config file

  Background:
    Given a valid migration.json configuration file with field-transform and node-structure tools enabled
    And an output package directory exists

  @us1-write-happy-path
  Scenario: CLI writes migration-config.json to the package root before submission
    When the operator runs "migrate queue export --output <packagePath>"
    Then migration-config.json exists at the package root
    And migration-config.json contains a "MigrationPlatform" top-level key
    And the job is submitted to the control plane after the file is written

  @us1-write-idempotency
  Scenario: CLI fails with a clear error when migration-config.json already exists
    Given migration-config.json already exists in the package
    When the operator runs "migrate queue export --output <packagePath>"
    Then the command exits with a non-zero status code
    And the error message references "already exists"
    And no duplicate job submission is made

  @us1-read-happy-path
  Scenario: Migration Agent reads config from package at job start
    Given migration-config.json exists in the package with FieldTransform configuration
    When the Migration Agent processes the job
    Then the FieldTransform tool receives configuration from the package config
    And no fallback to a local migration.json file occurs

  @us1-read-missing
  Scenario: Migration Agent throws PackageConfigNotFoundException for pre-025 packages
    Given migration-config.json does not exist in the package
    When the Migration Agent attempts to start the job
    Then a PackageConfigNotFoundException is thrown
    And the log contains "This package pre-dates config-in-package"
    And the job is marked as failed

  @us1-read-retry
  Scenario: Migration Agent retries reading config on eventual consistency delay
    Given migration-config.json is not immediately visible in the package store
    And migration-config.json becomes visible after 200ms
    When the Migration Agent attempts to read the config
    Then the config is eventually read successfully
    And a debug log entry records the retry
