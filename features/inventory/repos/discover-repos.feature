Feature: Discover Git repository counts in inventory
  As a migration operator
  I want the discovery inventory to count the Git repositories in each project
  So that I can see actual repository counts instead of zeros in the Repos column

  Background:
    Given an Azure DevOps organisation is reachable at the configured URL
    And the operator has supplied a valid Personal Access Token

  @azure-devops-rest
  Scenario: Repo count is included in the final inventory event for a project
    Given the project "Alpha" contains 3 Git repositories
    When the inventory service runs for project "Alpha"
    Then the final inventory event for "Alpha" has a ReposCount of 3

  @azure-devops-rest
  Scenario: Repo count of zero is reported correctly when a project has no repositories
    Given the project "Empty" contains 0 Git repositories
    When the inventory service runs for project "Empty"
    Then the final inventory event for "Empty" has a ReposCount of 0
