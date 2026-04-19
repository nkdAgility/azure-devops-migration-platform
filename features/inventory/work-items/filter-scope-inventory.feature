Feature: Filter Scope and WIQL Scope for Work Item Inventory
  As a platform engineer
  I want to add wiql and filter scopes to organisation entries in the inventory config
  So that inventory counts reflect only the work items relevant to each organisation's migration scope

  @simulated @offline @filter
  Scenario: Organisation with wiql scope uses custom query for inventory
    Given an inventory config with an organisation entry that has a wiql scope:
      | query                                                                                                                         |
      | SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.State] = 'Active' ORDER BY [System.Id]  |
    When inventory runs for that organisation
    Then the custom WIQL query is used for work item discovery
    And only active work items are counted

  @simulated @offline
  Scenario: Organisation with no wiql scope uses platform default query
    Given an inventory config with an organisation entry that has no wiql scope
    When inventory runs for that organisation
    Then the platform default query "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]" is used

  @simulated @offline
  Scenario: Organisation with empty wiql query falls back to platform default
    Given an inventory config with an organisation entry that has a wiql scope with an empty query parameter
    When inventory runs for that organisation
    Then the platform default query is used

  @simulated @offline @filter
  Scenario: Organisation with filter scope counts only matching work items
    Given an inventory config with an organisation entry that has a filter scope:
      | mode    | field             | pattern          |
      | include | System.AreaPath   | ^MyOrg\\TeamA    |
    And 4 work items exist in the project: 2 under "MyOrg\\TeamA" and 2 under "MyOrg\\Archived"
    When inventory runs for that organisation
    Then the inventory result count for that project is 2

  @simulated @offline @filter
  Scenario: Organisation with combined wiql and filter scope applies both constraints
    Given an inventory config with an organisation entry that has a wiql scope and a filter scope:
      | wiql_query                                                                                                                   | filter_field      | filter_pattern  | filter_mode |
      | SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.State] = 'Active' ORDER BY [System.Id] | System.AreaPath   | ^MyOrg\\TeamA   | include     |
    And 5 work items exist: 2 active under TeamA, 1 active under Archived, 2 closed under TeamA
    When inventory runs for that organisation
    Then the inventory result count for that project is 2

  @simulated @offline
  Scenario: Other organisations without scopes use platform defaults
    Given an inventory config with two organisation entries:
      | org   | has_scopes |
      | org-a | yes        |
      | org-b | no         |
    When inventory runs for both organisations
    Then org-a uses its configured scopes for discovery
    And org-b uses the platform default query with no filters

  @simulated @offline @filter
  Scenario: Filter scope unions filter field names with System.Rev in discovery request
    Given an inventory config with an organisation entry that has a filter scope on field "System.AreaPath"
    When inventory runs for that organisation
    Then the work item fetch request for that organisation includes both "System.Rev" and "System.AreaPath" in the fields list
