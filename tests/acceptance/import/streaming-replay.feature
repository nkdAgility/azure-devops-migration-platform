Feature: Streaming Import Replay
  As a migration operator
  I want the import process to replay work item revisions chronologically from the package
  So that the target project accurately reflects the historical state of the source project

  Background:
    Given a valid migration package exists at the configured package root
    And the package contains work item revision folders in canonical chronological order

  Scenario: Import replays revisions in chronological order determined by folder name
    Given the package contains revision folders in lexicographic order
    When the WorkItems import module runs
    Then each revision is applied to the target in the order determined by folder name ascending
    And no in-memory sort of revision paths is performed

  Scenario: Import processes one revision at a time without buffering all revisions in memory
    Given the package contains 50000 revision folders
    When the WorkItems import module runs
    Then revisions are enumerated and applied one folder at a time
    And all revision folder paths are never held in a single in-memory collection simultaneously

  Scenario: Import reads revision.json and applies all fields to the target work item
    Given a revision folder contains a "revision.json" with title, state, and assigned-to fields
    When the WorkItems import module processes that revision folder
    Then the target work item is updated with the title, state, and assigned-to from revision.json

  Scenario: Import resolves identities via IIdentityMappingService and not inline
    Given a revision.json contains an "assignedTo" field with a source identity
    When the WorkItems import module applies the revision
    Then the assigned-to value is resolved via IIdentityMappingService
    And no inline identity lookup occurs within the WorkItems module

  Scenario: Import calls only the target API and never the source API
    Given the import module is processing a revision folder
    When the import module applies the revision to the target
    Then only target-side API calls are made
    And no source Azure DevOps API calls are made during import

  Scenario: Import recreates the attachment on the target from the revision folder
    Given a revision folder contains "revision.json" and "screenshot.png"
    When the WorkItems import module processes the revision folder
    Then "screenshot.png" is uploaded to the target work item at the correct revision
    And the attachment metadata in the target matches the reference in "revision.json"
