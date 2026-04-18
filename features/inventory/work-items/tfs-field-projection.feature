Feature: TFS field projection
  As a migration operator running inventory against a TFS source
  I want the TFS fetch service to stream field-projected work items via the same interface
  So that inventory and dependency analysis work uniformly regardless of source type

  @tfs-object-model
  Scenario: TFS source streams items with requested fields
    Given a TFS project with work items of mixed types
    When inventory runs with a fetch scope specifying fields "System.WorkItemType" and "System.State"
    Then only those fields are included in each FetchedWorkItem
    And the results stream as IAsyncEnumerable without full-set buffering

  @tfs-object-model
  Scenario: TFS filter exclusion works in-process
    Given a TFS project with work items of types "Bug", "Task", and "Requirement"
    And a fetch scope with a filter restricting to type "Bug"
    When the TFS fetch service streams items
    Then only "Bug" items are yielded
    And other types are discarded in-process
