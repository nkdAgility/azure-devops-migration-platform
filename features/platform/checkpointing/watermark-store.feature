Feature: Work Item Watermark Store
  As a migration operator
  I want the platform to track how far the export has progressed for each work item
  So that an interrupted export can resume without reprocessing revisions that were already written

  Background:
    Given the watermark store has been initialised
    And the export has not yet begun

  Scenario: The watermark for a work item is recorded when its first revision is processed
    Given work item 42 has not been exported before
    When the export processes revision 0 of work item 42
    Then the watermark store records that work item 42 was last processed at revision 0

  Scenario: The watermark advances when a later revision of the same work item is processed
    Given the export has already processed up to revision 2 of work item 7
    When the export processes revision 3 of work item 7
    Then the watermark store records that work item 7 was last processed at revision 3

  Scenario: The watermark does not retreat when an earlier revision index is recorded
    Given the export has already processed up to revision 5 of work item 9
    When the export attempts to record progress at revision 3 of work item 9
    Then the watermark store still records that work item 9 was last processed at revision 5

  Scenario: Revisions at or below the watermark are considered already processed
    Given the export has already processed up to revision 4 of work item 12
    Then the platform considers revision 0 of work item 12 as already processed
    And the platform considers revision 4 of work item 12 as already processed

  Scenario: Revisions above the watermark are considered unprocessed
    Given the export has already processed up to revision 4 of work item 12
    Then the platform considers revision 5 of work item 12 as not yet processed
    And the platform considers revision 99 of work item 12 as not yet processed

  Scenario: A work item with no recorded progress is treated as fully unprocessed
    Given work item 100 has never been exported
    Then the platform considers all revisions of work item 100 as not yet processed

  Scenario: The WIQL query result count is cached to avoid redundant queries
    Given a WIQL query has not been run before
    When the export determines there are 1500 work items matching that query
    Then the platform stores 1500 as the cached count for that query
    And a subsequent check for the same query returns 1500 without querying the source again

  Scenario: A cached query count is replaced when a newer count is recorded
    Given the platform has cached a count of 100 for a WIQL query
    When the export records a new count of 200 for the same query
    Then the platform returns 200 as the cached count for that query

  Scenario: Watermarks for different work items are tracked independently
    Given the export has processed up to revision 2 of work item 1
    And the export has processed up to revision 7 of work item 2
    Then the platform considers revision 3 of work item 1 as not yet processed
    And the platform considers revision 7 of work item 2 as already processed

  Scenario: Recorded watermarks are available after the platform restarts
    Given the export has processed up to revision 3 of work item 55
    When the migration platform is restarted
    Then the platform still considers revision 3 of work item 55 as already processed
    And the platform still considers revision 4 of work item 55 as not yet processed
