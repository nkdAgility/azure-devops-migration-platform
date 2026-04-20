Feature: Filter Scope for Work Item Export
  As a migration engineer
  I want to add filter scopes to the WorkItems export module
  So that only work items whose field values match my criteria are exported to the package

  Background:
    Given a Simulated source endpoint with 2 projects and 3 work items per project
    And each work item has a "System.AreaPath" field

  @simulated @offline @filter
  Scenario: Include filter exports only matching work items
    Given a WorkItems module with a filter scope:
      | mode    | field             | pattern          |
      | include | System.AreaPath   | ^MyOrg\\TeamA    |
    And 2 work items have an AreaPath matching "^MyOrg\\TeamA" and 1 does not
    When the WorkItems export module runs
    Then the package contains exactly 2 work item directories under "WorkItems/"
    And a diagnostic log entry records each skipped work item with field "System.AreaPath" and mode "include"

  @simulated @offline @filter
  Scenario: Exclude filter omits matching work items
    Given a WorkItems module with a filter scope:
      | mode    | field             | pattern          |
      | exclude | System.AreaPath   | ^MyOrg\\Archived |
    And 1 work item has an AreaPath matching "^MyOrg\\Archived" and 2 do not
    When the WorkItems export module runs
    Then the package contains exactly 2 work item directories under "WorkItems/"
    And the archived work item is not present in the package

  @simulated @offline @filter
  Scenario: Multiple filter scopes are applied as AND conditions
    Given a WorkItems module with two filter scopes:
      | mode    | field              | pattern         |
      | include | System.AreaPath    | ^MyOrg          |
      | include | System.State       | Active          |
    And 1 work item matches both filters, 1 matches only AreaPath, 1 matches neither
    When the WorkItems export module runs
    Then the package contains exactly 1 work item directory under "WorkItems/"

  @simulated @offline @filter
  Scenario: Include filter rejects item when field is absent
    Given a WorkItems module with a filter scope:
      | mode    | field               | pattern  |
      | include | System.CustomField  | ^prefix  |
    And a work item does not have the "System.CustomField" field
    When the WorkItems export module runs
    Then the work item without "System.CustomField" is not written to the package

  @simulated @offline @filter
  Scenario: Exclude filter passes item when field is absent
    Given a WorkItems module with a filter scope:
      | mode    | field               | pattern  |
      | exclude | System.CustomField  | ^prefix  |
    And a work item does not have the "System.CustomField" field
    When the WorkItems export module runs
    Then the work item without "System.CustomField" is written to the package

  @simulated @offline @filter
  Scenario: Zero items pass filter — run completes with warning
    Given a WorkItems module with a filter scope:
      | mode    | field             | pattern              |
      | include | System.AreaPath   | ^NoMatchingAreaPath$ |
    When the WorkItems export module runs
    Then the run completes successfully
    And a warning is logged stating that zero work items passed the filter

  @simulated @offline @filter
  Scenario: Pre-filter pass uses only filter-referenced fields
    Given a WorkItems module with a filter scope on field "System.AreaPath"
    When the WorkItems export module runs
    Then the pre-filter fetch request includes "System.AreaPath" in the fields list
    And no revision history is fetched for work items that do not pass the filter

  @simulated @offline
  Scenario: No filter scopes — all work items exported (backward compatibility)
    Given a WorkItems module with no filter scopes
    When the WorkItems export module runs
    Then all 6 work items are written to the package
