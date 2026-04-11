Feature: Export Work Item Comments
  As a migration user
  I want to export all comments and discussions from work items
  So that comment history is preserved in the migration package

  Background:
    Given the test project is ready for export

  Scenario: Three comments exported to three separate folders
    Given a work item with ID 12345 exists in the source
    And the work item has 3 comments created on different dates
    When the export runs
    Then 3 comment folders are created with pattern "*-12345-c<commentId>/"
    And each folder contains a valid comment.json file
    And all comment metadata is preserved

  Scenario: Zero comments result in zero comment folders
    Given a work item with ID 54321 exists in the source
    And the work item has no comments
    When the export runs
    Then no comment folders are created for work item 54321
    And the work item revisions are still exported normally

  Scenario: Pagination handles more than one page of comments
    Given a work item with ID 99999 exists in the source
    And the work item has 150 comments (exceeding a typical page size of 100)
    When the export runs
    Then all 150 comments are exported across multiple comment folders
    And comment pagination cursor is properly managed

  Scenario: Resume cursor skips already-exported work items
    Given a work item with ID 11111 is already exported with 5 comments
    And a new work item with ID 22222 has 3 comments not yet exported
    When the export resumes
    Then work item 11111 comments are not re-exported
    And work item 22222 comments are exported
    And the cursor advances to work item 22222
