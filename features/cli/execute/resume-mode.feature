@cli @execute @resume
Feature: Resume Mode
  As a migration operator
  I need the CLI to support resuming interrupted exports and imports
  So that I can recover from failures without re-processing completed work

  Background:
    Given a valid migration package exists at the configured package root

  Scenario: Export resumes from cursor after interruption
    Given a previous export was interrupted after processing 5 of 10 work items
    And the checkpoint cursor records the last successfully exported revision folder
    When the export command runs again with the same configuration
    Then processing resumes from the revision after the cursor position
    And the first 5 work items are not re-processed

  Scenario: Import resumes from cursor after interruption
    Given a previous import was interrupted after importing 3 of 8 revision folders
    And the import checkpoint cursor records the last successfully imported folder
    When the import command runs again with the same configuration
    Then processing resumes from the folder after the cursor position
    And the first 3 folders are not re-imported to the target

  Scenario: Force-fresh flag resets cursor and reprocesses all
    Given a checkpoint cursor exists from a previous run
    When the CLI is invoked with --force-fresh
    Then the checkpoint cursor is deleted before processing begins
    And all work items are processed from the beginning

  Scenario: Completed cursor skips entire module
    Given the export checkpoint cursor is marked as Completed
    When the export command runs again
    Then no revisions are processed
    And the CLI exits successfully with a message indicating export already complete

  Scenario: InProgress cursor triggers re-processing from that position
    Given the export checkpoint cursor has state InProgress at position "2024-01-15/..."
    When the export command runs again
    Then processing resumes from the cursor position
    And the revision at the cursor position is re-processed (idempotent)
