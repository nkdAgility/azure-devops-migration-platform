Feature: WorkItems Referenced Paths Tracking
  As a platform operator
  I want WorkItemsModule to record every area path and iteration path encountered during export
  So that NodeStructureModule can pre-create those nodes on the target before work items are imported

  Background:
    Given a simulated source project with work items spanning multiple area and iteration paths

  @export @nodes @workitems
  Scenario: Export records all unique area paths from work item revisions
    Given 10 work items with area paths across "ProjectA", "ProjectA\\Team1", and "ProjectA\\Team2"
    When the WorkItems module exports the project
    Then "referenced-paths.json" contains "ProjectA", "ProjectA\\Team1", and "ProjectA\\Team2" area paths
    And duplicate paths appear only once in "referenced-paths.json"

  @export @nodes @workitems
  Scenario: Export records all unique iteration paths from work item revisions
    Given 10 work items with iteration paths across "ProjectA\\Sprint 1", "ProjectA\\Sprint 2", and "ProjectA\\Sprint 3"
    When the WorkItems module exports the project
    Then "referenced-paths.json" contains all three iteration paths
    And each path appears only once

  @export @nodes @workitems
  Scenario: Export accumulates paths across all revisions not just latest
    Given a work item with 3 revisions: first in "ProjectA\\OldArea", second in "ProjectA\\NewArea", third in "ProjectA\\NewArea"
    When the WorkItems module exports the work item
    Then "referenced-paths.json" contains both "ProjectA\\OldArea" and "ProjectA\\NewArea"

  @export @nodes @workitems
  Scenario: Referenced paths are written to the artefact store
    Given 5 work items with distinct area and iteration paths
    When the WorkItems module exports
    Then the artefact store contains "Nodes/referenced-paths.json"
    And the file is valid JSON
