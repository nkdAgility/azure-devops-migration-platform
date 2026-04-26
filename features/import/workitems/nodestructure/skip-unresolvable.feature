Feature: Skip or fail on unresolvable area and iteration paths
  As a migration operator
  I want to control whether revisions with unresolvable paths are skipped or cause a failure
  So that large migrations can succeed with minor path issues while critical projects fail fast

  Background:
    Given a NodeStructure configuration with no mapping rules

  Scenario: Revision is skipped when area path cannot be resolved and skip is enabled
    Given SkipOnUnresolvableArea is enabled
    And a revision with area path "OtherProject\Unknown" and iteration path "SourceProject\Sprint 1"
    When the revision is processed
    Then the revision is skipped with a warning

  Scenario: Revision is skipped when iteration path cannot be resolved and skip is enabled
    Given SkipOnUnresolvableIteration is enabled
    And a revision with area path "SourceProject\Team A" and iteration path "OtherProject\Unknown"
    When the revision is processed
    Then the revision is skipped with a warning

  Scenario: Import fails when area path is unresolvable and skip is disabled
    Given SkipOnUnresolvableArea is disabled
    And a revision with area path "OtherProject\Unknown" and iteration path "SourceProject\Sprint 1"
    When the revision is processed
    Then an error is raised identifying the unresolvable area path

  Scenario: External path warning identifies the path as external
    Given SkipOnUnresolvableArea is enabled
    And a revision with area path "ExternalProject\Node" and iteration path "SourceProject\Sprint 1"
    When the revision is processed
    Then the revision is skipped with a warning identifying the path as external
