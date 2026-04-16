Feature: Import Work Item Comments
  As a migration operator
  I want work item comments to be imported from the package
  So that the conversation history is preserved in the target project

  Background:
    Given a valid migration package exists at the configured package root
    And the package contains work item revision folders in canonical chronological order

  @comments
  Scenario: Comment sub-folders are imported into the correct target work item
    Given the package contains a comment folder with name matching "<ticks>-<workItemId>-c<commentId>"
    And the source work item ID is mapped to a target work item ID in idmap.db
    When the import processes that comment folder (Comments extension is enabled)
    Then the comment text from "comment.json" is created on the target work item via the Comments API
    And the cursor is written with stage "Completed" for that comment folder

  @comments
  Scenario: Comment sub-folders are skipped when Comments extension is disabled
    Given the package contains comment folders
    And the Comments extension is set to disabled in the module configuration
    When the import runs
    Then comment folders are skipped
    And the cursor is advanced past each comment folder without calling the Comments API

  @comments
  Scenario: Inline comments in revision folders are imported
    Given a revision folder contains a "comment.json" array with non-deleted comments
    And the Comments extension is enabled
    When the import processes that revision folder after Stage D
    Then each non-deleted comment is created on the target work item via the Comments API

  @comments
  Scenario: Deleted comments are not imported
    Given a revision folder contains a "comment.json" with a comment where "isDeleted" is true
    When the import processes the inline comments
    Then the deleted comment is not created on the target work item
