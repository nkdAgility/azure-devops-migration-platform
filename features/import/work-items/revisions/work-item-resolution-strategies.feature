Feature: Work Item Resolution Strategies
  As a migration operator
  I want to configure how source work items are matched to existing target work items
  So that I can resume an interrupted migration without creating duplicates

  Background:
    Given a valid migration package exists at the configured package root
    And the package contains work item revision folders

  @resolution-strategy @target-field
  Scenario: TargetField strategy seeds the ID map from existing target work items
    Given the WorkItemResolutionStrategy extension is configured as "TargetField" with fieldName "Custom.SourceId"
    And the target project contains work items with the custom field populated
    When the import starts
    Then a WIQL query retrieves all target work items with the custom field set
    And the idmap.db is seeded with source-to-target ID mappings from those results
    And no duplicate work items are created for already-mapped source IDs

  @resolution-strategy @target-field
  Scenario: TargetField strategy writes provenance to the target work item custom field
    Given the WorkItemResolutionStrategy extension is "TargetField" with fieldName "Custom.SourceId"
    And Stage A creates a new work item in the target
    When provenance is written after creation
    Then the target work item "Custom.SourceId" field is updated with the source work item ID

  @resolution-strategy @target-hyperlink
  Scenario: TargetHyperlink strategy seeds the ID map from hyperlinks on existing target work items
    Given the WorkItemResolutionStrategy extension is configured as "TargetHyperlink" with urlPattern "https://source.example.com/wi/{id}"
    And the target project contains work items with hyperlinks matching the URL pattern
    When the import starts
    Then all target work items with HyperLinkCount > 0 are fetched
    And hyperlinks matching the URL pattern are inspected to extract source work item IDs
    And the idmap.db is seeded with the resolved source-to-target mappings
    And no per-item live lookup is performed during processing

  @resolution-strategy @null
  Scenario: Default NullResolutionStrategy performs no seeding and no live lookup
    Given no WorkItemResolutionStrategy extension is configured
    When the import starts
    Then no WIQL query is executed for seeding
    And Stage A uses only the local idmap.db for resolution
    And null is returned for source IDs not present in idmap.db (triggering creation)
