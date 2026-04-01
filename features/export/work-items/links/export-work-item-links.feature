Feature: Export Work Item Links
  As a migration operator
  I want links exported as a delta per revision
  So that each revision.json captures only the links that were added in that revision

  Background:
    Given the source project contains work items with one or more link types
    And the export module is configured with valid source credentials

  @tfs-object-model
  Scenario: Only links added in the current revision are exported
    Given work item 10 has 2 revisions
    And revision 0 has no links
    And revision 1 adds a related link to work item 20
    When the WorkItems export module processes work item 10
    Then revision 0's "revision.json" contains an empty links collection
    And revision 1's "revision.json" contains exactly the new related link to work item 20

  @tfs-object-model
  Scenario: External link is captured with artifact URI and link type name
    Given revision 2 of work item 5 adds an external link with uri "vstfs:///Git/Commit/abc123" and type "Fixed in Changeset"
    When the WorkItems export module processes revision 2 of work item 5
    Then "revision.json" contains an external link entry with linkedArtifactUri "vstfs:///Git/Commit/abc123"
    And the external link entry records artifactLinkType "Fixed in Changeset"

  @tfs-object-model
  Scenario: Related link is captured with link type end name and related work item id
    Given revision 3 of work item 7 adds a related link to work item 42 with link type end "Child"
    When the WorkItems export module processes revision 3 of work item 7
    Then "revision.json" contains a related link entry with relatedWorkItemId 42
    And the related link entry records linkTypeEnd "Child"

  @tfs-object-model
  Scenario: Hyperlink is captured with its URL location
    Given revision 1 of work item 11 adds a hyperlink to "https://docs.example.com/spec"
    When the WorkItems export module processes revision 1 of work item 11
    Then "revision.json" contains a hyperlink entry with location "https://docs.example.com/spec"

  @tfs-object-model
  Scenario: Duplicate links that already exist in a previous revision are not re-exported
    Given revision 0 of work item 15 adds a related link to work item 30
    And revision 1 of work item 15 retains that same related link without adding any new link
    When the WorkItems export module processes work item 15
    Then revision 1's "revision.json" contains an empty links collection

  @tfs-object-model
  Scenario: Multiple link types added in the same revision are all exported
    Given revision 2 of work item 99 simultaneously adds one external link, one related link, and one hyperlink
    When the WorkItems export module processes revision 2 of work item 99
    Then "revision.json" contains exactly one external link entry
    And "revision.json" contains exactly one related link entry
    And "revision.json" contains exactly one hyperlink entry

  @tfs-object-model
  Scenario: An unrecognised link type causes the export to fail with a clear error
    Given revision 4 of work item 8 contains a link of an unsupported type
    When the WorkItems export module processes revision 4 of work item 8
    Then the export stops with a clear error identifying the unrecognised link type
    And no "revision.json" is written for that revision

  @tfs-object-model
  Scenario: Link metrics are recorded per link processed
    Given a revision adds 3 links of different types
    When the WorkItems export module processes that revision
    Then the platform records a successful export metric for each link
    And the platform records the processing duration for each link
