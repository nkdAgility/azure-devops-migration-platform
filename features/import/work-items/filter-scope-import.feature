Feature: Filter Scope for Work Item Import
  As a migration engineer
  I want to add filter scopes to the WorkItems import module
  So that only work items whose latest revision fields match my criteria are imported to the target

  Background:
    Given a package on the filesystem containing 3 work items
    And each work item has at least 2 revisions

  @simulated @offline @filter
  Scenario: Include filter imports only matching work items
    Given a WorkItems module configured for import with a filter scope:
      | mode    | field             | pattern          |
      | include | System.AreaPath   | ^MyOrg\\TeamA    |
    And work item 1 has a last revision with AreaPath "MyOrg\\TeamA\\Feature1"
    And work item 2 has a last revision with AreaPath "MyOrg\\Archived\\Bug1"
    And work item 3 has a last revision with AreaPath "MyOrg\\TeamA\\Bug2"
    When the WorkItems import module runs
    Then work items 1 and 3 are imported to the target
    And work item 2 is skipped

  @simulated @offline @filter
  Scenario: Filter evaluates the last revision only — not earlier revisions
    Given a WorkItems module configured for import with a filter scope:
      | mode    | field           | pattern   |
      | include | System.State    | Active    |
    And work item 1 has an earlier revision with State "Closed" and a last revision with State "Active"
    When the WorkItems import module runs
    Then work item 1 is imported to the target
    And the earlier revision with State "Closed" does not cause the item to be skipped

  @simulated @offline @filter
  Scenario: Skipped work item produces a diagnostic log entry
    Given a WorkItems module configured for import with a filter scope:
      | mode    | field             | pattern          |
      | include | System.AreaPath   | ^MyOrg\\TeamA    |
    And work item 2 has a last revision with AreaPath "MyOrg\\Archived\\Bug1"
    When the WorkItems import module runs
    Then a diagnostic log entry is written for work item 2 recording field "System.AreaPath" and mode "include"

  @simulated @offline @filter
  Scenario: Zero items pass filter — import completes with warning
    Given a WorkItems module configured for import with a filter scope:
      | mode    | field             | pattern                |
      | include | System.AreaPath   | ^NoMatchingAreaPath$   |
    When the WorkItems import module runs
    Then the run completes successfully
    And a warning is logged stating that zero work items passed the filter

  @simulated @offline
  Scenario: No filter scopes — all work items imported (backward compatibility)
    Given a WorkItems module configured for import with no filter scopes
    When the WorkItems import module runs
    Then all 3 work items are imported to the target
