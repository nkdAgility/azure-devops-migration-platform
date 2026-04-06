Feature: System Test Local Execution for Inventory Command
  As a developer
  I want to run system tests locally against live Azure DevOps organizations
  So that I can validate inventory command functionality during development

  Background:
    Given I am working in a local development environment
    And the system test framework is available

  @local @system-test
  Scenario: Developer runs system test with valid environment configuration
    Given I have set environment variable AZDEVOPS_SYSTEM_TEST_ORG to a valid organization name
    And I have set environment variable AZDEVOPS_SYSTEM_TEST_PAT to a valid personal access token
    When I run "dotnet test --filter TestCategory=SystemTest"
    Then the system test should execute successfully
    And the inventory command should connect to the Azure DevOps organization
    And the test result should indicate success
    And the execution time should be under 30 seconds

  @local @system-test
  Scenario: Developer runs system test with missing environment variables
    Given I have not configured environment variables for system tests
    When I run "dotnet test --filter TestCategory=SystemTest" 
    Then the system test should be marked as inconclusive
    And the test output should display "System test skipped: Environment variables not configured"
    And the test output should include setup instructions referencing docs/contributors.md
    And the overall test run should continue without failure

  @local @system-test  
  Scenario: Developer runs system test with invalid credentials
    Given I have set environment variable AZDEVOPS_SYSTEM_TEST_ORG to a valid organization name
    And I have set environment variable AZDEVOPS_SYSTEM_TEST_PAT to an invalid token
    When I run "dotnet test --filter TestCategory=SystemTest"
    Then the system test should be marked as inconclusive  
    And the test output should indicate "Authentication failed for organization"
    And the test output should reference troubleshooting documentation
    And no sensitive token information should appear in test output

  @local @system-test
  Scenario: Developer filters out system tests from regular test runs
    Given I have a mixed test suite with unit tests and system tests
    When I run "dotnet test --filter TestCategory!=SystemTest"
    Then only unit tests should execute
    And system tests should be excluded from execution
    And the test run should complete normally

  @local @system-test @cleanup
  Scenario: Developer system test creates and cleans up temporary artifacts
    Given I have properly configured environment variables for system tests
    When I run a system test that creates temporary files and directories
    Then the test should create artifacts in the system temp directory
    And all temporary artifacts should be cleaned up after test completion
    And no test artifacts should persist after test execution