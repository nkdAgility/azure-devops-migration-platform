Feature: Cursor-Based Resume and Checkpointing
  As a migration operator
  I want the migration to checkpoint its progress after each unit of work
  So that an interrupted run can resume from where it left off without reprocessing completed items

  Background:
    Given the migration module writes cursor files to "Checkpoints/<module>.cursor.json"

  Scenario: Cursor file is created on the first successful write
    Given no cursor file exists for the WorkItems module
    When the WorkItems module successfully processes its first revision folder
    Then "Checkpoints/workitems.cursor.json" is created
    And the cursor records the path of the last successfully processed folder

  Scenario: Cursor file is updated after each successfully processed revision
    Given the WorkItems module has already processed 10 revision folders
    When the WorkItems module processes the 11th revision folder
    Then "Checkpoints/workitems.cursor.json" is updated to record the 11th folder path

  Scenario: Resume skips all revision folders up to and including the cursor position
    Given "Checkpoints/workitems.cursor.json" records "WorkItems/2024-03-10/00638500000000-100-4/"
    When the WorkItems module starts on a second run
    Then all folders lexicographically less than or equal to "WorkItems/2024-03-10/00638500000000-100-4/" are skipped
    And the module resumes processing from the next folder after the cursor position

  Scenario: A run with no cursor starts from the beginning of the package
    Given "Checkpoints/workitems.cursor.json" does not exist
    When the WorkItems module starts
    Then the module processes all revision folders from the first lexicographic path

  Scenario: A crashed run leaves the cursor at the last successfully processed folder
    Given the WorkItems module has processed 20 revision folders
    And the process crashes while processing the 21st folder
    When the WorkItems module is restarted
    Then the cursor still records the 20th folder path
    And the 21st folder is reprocessed from the beginning

  Scenario: Cursor is written through IStateStore and not directly to the filesystem
    Given the WorkItems module is processing revisions
    When the cursor is updated
    Then the cursor write goes through IStateStore
    And no direct System.IO.File call is made for cursor persistence

  Scenario: Multiple modules maintain independent cursors without interference
    Given both the WorkItems module and the AreaPaths module are running
    When each module processes its respective data
    Then "Checkpoints/workitems.cursor.json" and "Checkpoints/areapaths.cursor.json" are independent files
    And updating one cursor does not affect the other
