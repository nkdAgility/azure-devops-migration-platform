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
