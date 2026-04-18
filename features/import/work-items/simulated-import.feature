Feature: Simulated Work Item Import
  As a migration developer
  I want to import work items to a Simulated target
  So that I can test the import pipeline without writing to a real Azure DevOps instance

  @simulated @offline
  Scenario: Import assigns sequential target IDs starting at 1
    Given a migration package with 3 work items
    And the target is configured as Simulated
    When the WorkItems import module runs
    Then work items are created with target IDs 1, 2, and 3

  @simulated @offline
  Scenario: Import emits progress events for each work item
    Given a migration package with 5 work items
    And the target is configured as Simulated
    When the WorkItems import module runs
    Then 5 progress events are emitted

  @simulated @offline
  Scenario: Import does not write to any external system
    Given a migration package with work items
    And the target is configured as Simulated
    When the WorkItems import module runs
    Then no HTTP requests are made
    And no database writes occur outside the local package

  @simulated @offline
  Scenario: Import validates workItemType is not empty
    Given a revision.json with an empty System.WorkItemType field
    And the target is configured as Simulated
    When the WorkItems import module runs
    Then an ArgumentException is thrown for the invalid work item type

  @simulated @offline
  Scenario: SimulatedWorkItemImportTargetFactory rejects non-Simulated endpoints
    Given an AzureDevOpsEndpointOptions endpoint
    When CreateAsync is called on SimulatedWorkItemImportTargetFactory
    Then an ArgumentException is thrown
