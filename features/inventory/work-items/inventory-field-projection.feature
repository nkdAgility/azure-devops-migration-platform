Feature: Inventory field projection
  As a migration operator running discovery inventory
  I want the system to fetch only the fields required for counting and filtering
  So that inventory scans complete faster and use less memory

  @azure-devops-rest
  Scenario: Inventory applies type filter in-process
    Given a project with work items of types "Bug", "Task", and "Epic"
    And a fetch scope with a filter restricting to type "Bug"
    When inventory runs
    Then only "Bug" items are counted
    And other types are discarded in-process without being written to any store
