Feature: Inventory field projection
  As a migration operator running discovery inventory
  I want the system to fetch only the fields required for counting and filtering
  So that inventory scans complete faster and use less memory

  @azure-devops-rest
  Scenario: Inventory fetches only declared fields
    Given a project with work items of mixed types
    When inventory runs with a fetch scope specifying fields "System.WorkItemType" and "System.State"
    Then only those fields are requested per work item from the source API
    And the inventory count is accurate

  @azure-devops-rest
  Scenario: Inventory applies type filter in-process
    Given a project with work items of types "Bug", "Task", and "Epic"
    And a fetch scope with a filter restricting to type "Bug"
    When inventory runs
    Then only "Bug" items are counted
    And other types are discarded in-process without being written to any store

  @azure-devops-rest
  Scenario: Inventory streams results with bounded memory
    Given a project with more than 20000 work items
    When inventory streams via the work item fetch service
    Then no full result set is held in memory at any point
    And results are yielded one batch at a time
