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
  Scenario: Work item and revision counts stream incrementally as each batch is processed
    Given the project "Alpha" contains 45000 work items with revisions
    When the platform counts work items for project "Alpha"
    Then progress updates are sent before counting is complete
    And each intermediate update includes a non-zero work item count
    And the final update indicates that counting is complete for that project

  @azure-devops-rest
  Scenario: Work items are counted incrementally without loading all IDs into memory
    Given the project "Beta" contains 50000 work items
    When the platform counts work items for project "Beta"
    Then the platform fetches work items in batches rather than all at once
    And each batch starts from after the last work item of the previous batch
    And at no point are all 50000 work item IDs held in memory simultaneously

  @azure-devops-rest
  Scenario: Revision count reflects the total revisions across all work items in the project
    Given work items in project "Gamma" have an average of 4 revisions each
    And there are 1000 work items in "Gamma"
    When the platform finishes counting work items for project "Gamma"
    Then the final count for "Gamma" shows approximately 4000 revisions

  @azure-devops-rest
  Scenario: An organisation with no work items in a project yields a single complete update
    Given the project "Empty" has no work items
    When the platform counts work items for project "Empty"
    Then a single count update is provided showing 0 work items and 0 revisions
    And the update indicates that counting is complete

  @azure-devops-rest
  Scenario: Each progress update includes the time it was recorded
    Given the platform is counting work items for a project
    When a progress update is sent
    Then each progress update includes the time it was recorded in UTC

  @azure-devops-rest
  Scenario: Discovery results for a completed run can be saved to a CSV file
    Given the platform has finished counting all projects in the organisation
    When the operator requests a CSV export of the discovery summary
    Then a file named "inventory.csv" is created
    And each row records the project name, work item count, revision count, repo count, and pipeline count
