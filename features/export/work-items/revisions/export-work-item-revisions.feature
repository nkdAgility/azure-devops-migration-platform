Feature: Export Work Item Revisions
  As a migration operator
  I want to export all work item revisions from a source Azure DevOps project
  So that the package contains a complete, chronologically ordered record of every change

  Background:
    Given the source project contains work items with multiple revisions
    And the export module is configured with valid source credentials

  @azure-devops-rest @tfs-object-model
  Scenario: Export a single work item with multiple revisions to the canonical folder layout
    Given a work item with id 42 has 3 revisions
    When the WorkItems export module runs
    Then the package contains folders matching the pattern "WorkItems/yyyy-MM-dd/<ticks>-42-0/", "WorkItems/yyyy-MM-dd/<ticks>-42-1/", and "WorkItems/yyyy-MM-dd/<ticks>-42-2/"
    And each folder contains a "revision.json" file
    And the folders are ordered lexicographically ascending by folder name

  Scenario: Export writes only via IArtefactStore and never accesses the filesystem directly
    Given the export module is configured
    When the WorkItems export module runs
    Then all file writes go through IArtefactStore
    And no direct System.IO calls are made inside the module

  Scenario: Export records a cursor after each revision is written
    Given the export module begins writing revision folders
    When the export module successfully writes a revision folder
    Then the cursor file at "Checkpoints/workitems.cursor.json" is updated with the last processed revision path

  Scenario: Export resumes from the cursor after an interruption
    Given the cursor file at "Checkpoints/workitems.cursor.json" records the last processed folder as "WorkItems/2024-01-15/00638412345678-42-1/"
    When the export module is re-run
    Then the export skips all revision folders at or before "WorkItems/2024-01-15/00638412345678-42-1/"
    And the export continues from the next unprocessed revision

  Scenario: Export with zero revisions writes no folders and no cursor
    Given a source project with no work items
    When the WorkItems export module runs
    Then no folders are created under "WorkItems/"
    And no cursor file is created

  Scenario: Export of a large work item set does not load all revisions into memory
    Given the source project contains 50000 work item revisions
    When the WorkItems export module runs
    Then work item revisions are processed one at a time
    And peak memory usage does not grow proportionally to the total revision count
