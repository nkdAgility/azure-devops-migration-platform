@feature:idmap-integrity-check @phase:import
Feature: ID Map Integrity Check
  As a migration engineer
  I want the import pipeline to check for stale ID mappings at startup
  So that I am warned before import begins if mapped target work items no longer exist

  Background:
    Given a migration package with idmap.db containing work item mappings

  @offline
  Scenario: Warning is logged for each mapping pointing to a deleted target work item
    Given idmap.db contains mappings:
      | sourceId | targetId |
      | 1        | 100      |
      | 2        | 200      |
    And target work item 100 does not exist in the target system
    And target work item 200 exists in the target system
    When the import pipeline runs CheckIntegrityAsync
    Then a warning is logged for the mapping source 1 → target 100
    And no warning is logged for the mapping source 2 → target 200
    And the import pipeline continues (integrity check is non-blocking)

  @offline
  Scenario: No warnings logged when all mappings are valid
    Given idmap.db contains a mapping source 5 → target 500
    And target work item 500 exists in the target system
    When the import pipeline runs CheckIntegrityAsync
    Then no integrity warnings are logged
    And the import pipeline continues normally
