Feature: Post-flight correctness metrics
  As a migration operator
  I want revision count parity and broken link detection metrics after import
  So that I can verify migration correctness without manual comparison

  Background:
    Given a migration configuration targeting the Simulated source
    And the configuration specifies operation "both" for module "workitems"

  @simulated
  Scenario: Matching revision counts produce zero missing and zero delta
    Given 20 work items each with matching source and target revision counts
    When post-flight validation runs
    Then the "migration.revisions.missing" counter equals 0
    And the "migration.revision.delta" histogram has a mean of 0

  @simulated
  Scenario: Fewer target revisions increment the missing counter
    Given 20 work items where 2 items have fewer target revisions than source
    When post-flight validation runs
    Then the "migration.revisions.missing" counter equals 2
    And the "migration.revision.delta" histogram records negative values for the affected items

  @simulated
  Scenario: Broken links are detected and counted
    Given 20 work items where 3 links reference non-existent target work items
    When post-flight validation runs
    Then the "migration.workitems.broken_links" counter equals 3

  @simulated
  Scenario: Sample rate zero skips all correctness checks
    Given a migration configuration with validation sample rate set to 0
    When post-flight validation runs
    Then no correctness metrics are emitted
