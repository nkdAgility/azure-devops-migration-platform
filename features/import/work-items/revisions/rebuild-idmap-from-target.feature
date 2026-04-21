@feature:rebuild-idmap-from-target @phase:import
Feature: Rebuild ID Map From Target
  As a migration engineer
  I want the ID map to be seeded from the target's provenance markers at import startup
  So that a lost or corrupted idmap.db does not cause duplicate work item creation

  Background:
    Given a migration package with work item revision folders
    And the target project contains work items with provenance field "Custom.SourceWorkItemId" populated

  @offline
  Scenario: ID map is seeded from target provenance markers before import begins
    Given source work items 10, 20, and 30 have been previously imported to the target
    And the target work items have provenance field "Custom.SourceWorkItemId" set to 10, 20, and 30
    And idmap.db is empty
    When the import pipeline starts
    Then idmap.db contains mappings for source IDs 10, 20, and 30
    And no duplicate work items are created for those source IDs

  @offline
  Scenario: Existing idmap.db mappings are not overwritten during seed
    Given source work item 10 is mapped to target 99 in idmap.db
    And the target project has a provenance mapping source 10 → target 100
    When the import pipeline starts
    Then idmap.db still maps source 10 to target 99
    And INSERT OR IGNORE semantics preserve the existing mapping
