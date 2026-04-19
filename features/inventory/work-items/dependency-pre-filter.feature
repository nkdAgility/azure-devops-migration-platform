Feature: Dependency pre-filter before Relations expand
  As a migration operator running dependency analysis
  I want the system to pre-filter work items by field before fetching Relations
  So that Relations API calls are only made for relevant items

  @azure-devops-rest
  Scenario: Only matching items trigger Relations expansion
    Given a project with 100 work items of types "Bug", "Task", and "Epic"
    And a fetch scope with a filter restricting to type "Bug"
    When dependency analysis runs
    Then Relations are fetched only for "Bug" items
    And "Task" and "Epic" items are never expanded

  @azure-devops-rest
  Scenario: Non-matching items are not yielded as dependency events
    Given a project with work items of types "Bug" and "Task"
    And a fetch scope with a filter restricting to type "Bug"
    When dependency analysis runs
    Then no DependencyFoundEvent is emitted for "Task" items
    And only "Bug" items appear in the dependency results

  @azure-devops-rest
  Scenario: Caller owns relation expansion separately from field fetch
    Given a project with work items
    When dependency analysis streams items via the fetch service
    Then the fetch service returns field-projected items without Relations
    And the dependency service performs Relations expansion as a second pass
