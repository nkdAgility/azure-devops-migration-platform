@cli @inventory @system-test @ci
Feature: System Test CI Execution
  As a platform contributor running system tests in CI
  I need system tests to execute securely with proper credential handling
  So that the CI pipeline validates real Azure DevOps connectivity without leaking secrets

  Background:
    Given the CI environment has AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT configured

  Scenario: System tests execute in CI environment with secrets
    When the system test runner detects CI environment variables
    Then the inventory command connects to the configured Azure DevOps organisation
    And the command produces valid output without errors

  Scenario: System tests skip gracefully when secrets are missing
    Given AZDEVOPS_SYSTEM_TEST_PAT is not set
    When a system test with [TestCategory("SystemTest_Live")] attempts execution
    Then the test reports a clear skip reason referencing docs/contributors.md
    And the CI pipeline continues without failure

  Scenario: No credentials appear in test output or logs
    When a system test executes against a live Azure DevOps organisation
    Then the test output does not contain the PAT value
    And the test output does not contain any bearer tokens
    And structured log entries mask credential fields

  Scenario: Network resilience in CI with timeout and retry
    Given the CI environment has intermittent network latency
    When a system test encounters a transient HTTP failure
    Then the test retries with exponential back-off
    And the total test execution completes within 5 minutes

  Scenario: Conditional execution based on environment
    Given no AZDEVOPS_SYSTEM_TEST_ORG environment variable is set
    When the test framework evaluates [TestCategory("SystemTest_Live")] tests
    Then those tests are reported as inconclusive rather than failed
    And unit tests continue to execute normally
