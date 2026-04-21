@feature:prevent-duplicate-work-items @phase:import
Feature: Prevent Duplicate Work Items During Import
  As a migration engineer
  I want the import pipeline to detect when a mapped target work item no longer exists
  So that we avoid infinite retry loops on a corrupt ID mapping

  Background:
    Given a migration package with work item revision folders
    And the idmap.db contains existing source-to-target mappings

  @offline
  Scenario: Stage A records a skip when the mapped target work item has been deleted
    Given source work item 42 is mapped to target work item 100 in idmap.db
    And target work item 100 does not exist in the target system
    When the import pipeline processes the revision folder for source work item 42
    Then a TargetWorkItemDeleted entry is recorded in idmap.db skipped_revisions
    And the cursor is advanced past the folder as Completed
    And no attempt is made to create a duplicate work item

  @offline
  Scenario: Stage A creates a new work item when no mapping exists
    Given source work item 43 has no mapping in idmap.db
    When the import pipeline processes the revision folder for source work item 43
    Then a new target work item is created
    And the source-to-target mapping is recorded in idmap.db

  @offline
  Scenario: Stage A skips creation when a valid existing mapping is found
    Given source work item 44 is mapped to target work item 200 in idmap.db
    And target work item 200 exists in the target system
    When the import pipeline processes the revision folder for source work item 44
    Then no new work item is created
    And the existing mapping is preserved in idmap.db
