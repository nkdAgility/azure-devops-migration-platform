Feature: Preserve iteration node start and finish dates during replication
  As a migration operator
  I want iteration sprint dates preserved when replicating the source tree
  So that sprint planning information is not lost after migration

  Background:
    Given a NodeStructure configuration with ReplicateSourceTree enabled
    And a package containing Nodes/source-tree.json

  Scenario: Iteration dates are set after node creation
    Given the source-tree artifact contains iteration node "SourceProject\Sprint 1" with start "2024-01-15" and finish "2024-01-28"
    When the replicate-source-tree phase runs
    Then SetIterationDates is called for "TargetProject\Sprint 1" with start "2024-01-15" and finish "2024-01-28"

  Scenario: Area nodes do not have dates set
    Given the source-tree artifact contains area node "SourceProject\Team A"
    When the replicate-source-tree phase runs
    Then SetIterationDates is not called for any node

  Scenario: Iteration node with null dates does not call SetIterationDates
    Given the source-tree artifact contains iteration node "SourceProject\Sprint 2" with no dates
    When the replicate-source-tree phase runs
    Then SetIterationDates is not called for any node

  Scenario: API failure setting iteration dates is logged as a warning
    Given the source-tree artifact contains iteration node "SourceProject\Sprint 1" with start "2024-01-15" and finish "2024-01-28"
    And SetIterationDates throws an exception
    When the replicate-source-tree phase runs
    Then the replication completes without throwing
    And a warning is logged for the date-setting failure
