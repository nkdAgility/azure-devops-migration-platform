@feature:revision-level-progress-tracking @phase:import
Feature: Revision-Level Progress Tracking
  As a migration engineer
  I want the import pipeline to track the last successfully applied revision per work item
  So that a partial re-run can skip revisions that were already imported

  Background:
    Given a migration package with work item revision folders

  @offline
  Scenario: last_revision_index is updated after a revision is applied
    Given source work item 1 has no last_revision_index in idmap.db
    And the package contains revision folder for WI 1 revision 2
    When the import pipeline successfully processes the revision 2 folder
    Then idmap.db shows last_revision_index = 2 for source work item 1

  @offline
  Scenario: last_revision_index is updated monotonically (never decremented)
    Given source work item 1 has last_revision_index = 5 in idmap.db
    When UpdateLastRevisionIndexAsync is called with revisionIndex = 3
    Then idmap.db still shows last_revision_index = 5 for source work item 1

  @offline
  Scenario: Revision folders below the watermark are skipped on re-run
    Given source work item 2 has last_revision_index = 4 in idmap.db
    And the package contains revision folders for WI 2 revisions 3, 4, and 5
    When the import pipeline runs
    Then only revision 5 for WI 2 is processed
    And revisions 3 and 4 are skipped via the revision-index watermark

  @offline
  Scenario: Comment folders are never skipped by the revision-index watermark
    Given source work item 3 has last_revision_index = 10 in idmap.db
    And the package contains a comment folder for WI 3
    When the import pipeline runs
    Then the comment folder for WI 3 is processed normally
    And the revision-index watermark does not affect comment folder processing
