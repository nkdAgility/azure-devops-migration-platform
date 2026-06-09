@cli @inventory @system-test @ci
Feature: System Test CI Execution
  As a platform contributor running system tests in CI
  I need system tests to execute securely with proper credential handling
  So that the CI pipeline validates real Azure DevOps connectivity without leaking secrets

  Background:
    Given the CI environment has AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT configured

  # Scenarios 2, 3, 4, and 5 have been retired — code-first MSTest tests pass in
  # tests/DevOpsMigrationPlatform.CLI.Migration.Tests/SystemTests/SystemTestCiExecutionTests.cs

  Scenario: System tests execute in CI environment with secrets
    When the system test runner detects CI environment variables
    Then the inventory command connects to the configured Azure DevOps organisation
    And the command produces valid output without errors
