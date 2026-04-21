@feature:rerun-delta-import @phase:import
Feature: Rerun Delta Import
  As a migration engineer
  I want to re-run the import and only process new or unprocessed revision folders
  So that incremental exports can be imported without reprocessing all existing work items

  Background:
    Given a migration package with multiple work item revision folders

  @offline
  Scenario: Previously completed revision folders are skipped on re-run
    Given revision folder for WI 1 revision 0 has been applied (cursor = Completed)
    And revision folder for WI 1 revision 1 is new
    When the import pipeline runs in Resume mode
    Then only the revision 1 folder for WI 1 is processed
    And the revision 0 folder for WI 1 is skipped

  @offline
  Scenario: Revision-index watermark prevents replaying already-applied revisions
    Given source work item 1 has last_revision_index = 3 in idmap.db
    And the package contains revision folders for WI 1 revisions 0, 1, 2, 3, and 4
    When the import pipeline runs
    Then only revision folder 4 for WI 1 is processed
    And revisions 0, 1, 2, and 3 are skipped via the revision-index watermark

  @offline
  Scenario: ForceFresh deletes cursor but preserves idmap.db
    Given a prior run has written a cursor and populated idmap.db with mappings
    When the import pipeline runs in ForceFresh mode
    Then the cursor is deleted
    And idmap.db still contains the existing mappings
    And all revision folders are processed from the beginning
