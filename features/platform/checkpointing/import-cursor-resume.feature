Feature: Import Cursor Resume
  As a migration operator
  I want the import to be resumable after interruption
  So that I can restart without reprocessing already-completed work

  Background:
    Given a valid migration package exists at the configured package root
    And the package contains work item revision folders in canonical chronological order

  @cursor-resume
  Scenario: Interrupted import resumes from the last cursor position
    Given an import has previously processed some revision folders
    And a cursor file exists at ".migration/Checkpoints/workitems.cursor.json" with stage "Completed"
    When the import is restarted
    Then all revision folders at or before the cursor "lastProcessed" value are skipped
    And import processing resumes from the first folder after the cursor position

  @cursor-resume
  Scenario: Mid-folder resume continues from the interrupted stage
    Given an import was interrupted after completing stage "AppliedFields" for a revision folder
    And the cursor file records stage "AppliedFields" for that folder
    When the import is restarted
    Then the importer skips stages "CreatedOrUpdated" and "AppliedFields" for that folder
    And processing continues from stage "AppliedLinks" within the same revision folder

  @cursor-resume
  Scenario: Force-fresh deletes the cursor but preserves the ID map
    Given an existing cursor file at ".migration/Checkpoints/workitems.cursor.json"
    And an existing ID map database at ".migration/Checkpoints/idmap.db"
    When the import is run with the "--force-fresh" flag
    Then the cursor file is deleted before import begins
    And the ID map database is preserved
    And import processing starts from the first revision folder in the package
