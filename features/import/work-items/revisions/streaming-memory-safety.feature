Feature: Streaming Memory-Safe Import
  As a migration operator importing a large package
  I want the import to process one revision folder at a time
  So that memory usage stays constant regardless of package size

  Background:
    Given a valid migration package exists at the configured package root
    And the package contains work item revision folders in canonical chronological order

  @memory-safety
  Scenario: Import processes revision folders one at a time
    Given the package contains 20000 revision folders
    When the WorkItems import module runs
    Then only one revision folder is in memory at any given time
    And the import does not require loading all revision folders before processing begins

  @memory-safety
  Scenario: Import uses EnumerateAsync without in-memory sorting
    Given the package contains revision folders returned in lexicographic order by the artefact store
    When the WorkItems import module enumerates the WorkItems folder
    Then folders are processed in the order returned by EnumerateAsync
    And no in-memory sorting or buffering of folder paths is performed

  @memory-safety
  Scenario: Attachment upload streams binary content without full in-memory buffer
    Given a revision folder contains an attachment binary file
    When the importer processes the attachment in Stage D
    Then the binary content is read as a stream from the artefact store
    And the stream is passed directly to the upload method without loading all bytes into memory
