Feature: Auto-create missing classification nodes
  As a migration operator
  I want missing area and iteration nodes to be created automatically
  So that work item import succeeds even when the target has no classification structure

  Background:
    Given a NodeStructure configuration with AutoCreateNodes enabled
    And a source package with referenced-paths.json

  Scenario: Missing area node is created before import
    Given the referenced-paths artifact contains area path "TargetProject\Team A"
    And the area node "TargetProject\Team A" does not exist in the target
    When the pre-collection phase runs
    Then the area node "TargetProject\Team A" is created in the target

  Scenario: Existing node is not created again
    Given the referenced-paths artifact contains area path "TargetProject\Team A"
    And the area node "TargetProject\Team A" already exists in the target
    When the pre-collection phase runs
    Then the node creator is called exactly once for "TargetProject\Team A"

  Scenario: AutoCreateNodes disabled skips pre-collection
    Given a NodeStructure configuration with AutoCreateNodes disabled
    And the referenced-paths artifact contains area path "TargetProject\Team A"
    When the pre-collection phase runs
    Then no nodes are created in the target

  Scenario: Empty package skips pre-collection gracefully
    Given the referenced-paths artifact contains no paths
    When the pre-collection phase runs
    Then no nodes are created in the target
