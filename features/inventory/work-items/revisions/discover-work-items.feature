Feature: Discover Work Items in an Azure DevOps Organisation
  As a migration operator
  I want to count all work items and revisions across every project in an organisation
  So that I can understand the scope of a migration before it begins

  Background:
    Given an Azure DevOps organisation is reachable at the configured URL
    And the operator has supplied a valid Personal Access Token

  @azure-devops-rest
  Scenario: All projects in the organisation are listed before counting begins
    Given the Azure DevOps organisation contains projects "Alpha", "Beta", and "Gamma"
    When the platform lists all projects in the organisation
    Then the result contains "Alpha", "Beta", and "Gamma"
    And no work item counts are included in the project list

  @azure-devops-rest
  Scenario: Each progress update includes the time it was recorded
    Given the platform is counting work items for a project
    When a progress update is sent
    Then each progress update includes the time it was recorded in UTC
