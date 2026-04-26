Feature: Export-time area and iteration path discovery
  As a migration operator
  I want export to record all distinct area and iteration paths from exported work items
  So that import can pre-create nodes without scanning all revision files

  Background:
    Given an empty artefact store for path discovery

  Scenario: Newly discovered area path is written to the package
    Given no referenced-paths artifact exists in the package
    When the path tracker discovers area path "ProjectA\Team A"
    Then the referenced-paths artifact contains area path "ProjectA\Team A"

  Scenario: Duplicate path is not written twice
    Given the referenced-paths artifact already contains area path "ProjectA\Team A"
    When the path tracker discovers area path "ProjectA\Team A"
    Then the referenced-paths artifact contains exactly 1 area path

  Scenario: Case-insensitive deduplication
    Given the referenced-paths artifact already contains area path "ProjectA\Team A"
    When the path tracker discovers area path "PROJECTA\TEAM A"
    Then the referenced-paths artifact contains exactly 1 area path

  Scenario: Final artifact contains all distinct area and iteration paths
    Given no referenced-paths artifact exists in the package
    When the path tracker discovers area path "ProjectA\Team A"
    And the path tracker discovers iteration path "ProjectA\Sprint 1"
    And the path tracker discovers area path "ProjectA\Team B"
    Then the referenced-paths artifact contains 2 area paths
    And the referenced-paths artifact contains 1 iteration path

  Scenario: Resume loads existing artifact paths
    Given the referenced-paths artifact already contains area path "ProjectA\Team A"
    When the path tracker is initialized from the existing artifact
    And the path tracker discovers area path "ProjectA\Team A"
    Then the referenced-paths artifact contains exactly 1 area path
