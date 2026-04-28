Feature: Replicate source classification tree to target
  As a migration operator
  I want the complete source area and iteration tree replicated to the target
  So that all paths exist before any work item revision is written

  Background:
    Given a NodeTranslation configuration with ReplicateSourceTree enabled
    And a package containing Nodes/source-tree.json

  Scenario: All area and iteration nodes are created before import
    Given the source-tree artifact contains area node "SourceProject\Team A"
    And the source-tree artifact contains iteration node "SourceProject\Sprint 1" with no dates
    When the replicate-source-tree phase runs
    Then the area node "TargetProject\Team A" is created in the target
    And the iteration node "TargetProject\Sprint 1" is created in the target

  Scenario: ReplicateSourceTree disabled skips replication
    Given a NodeTranslation configuration with ReplicateSourceTree disabled
    When the replicate-source-tree phase runs
    Then no nodes are created in the target

  Scenario: Resume after interruption skips already confirmed nodes
    Given the source-tree artifact contains area node "SourceProject\Team A"
    And "TargetProject\Team A" is already in the node replication checkpoint
    When the replicate-source-tree phase runs
    Then no additional nodes are created for "TargetProject\Team A"

  Scenario: Source-tree artifact absent produces a warning and skips
    Given the source-tree artifact is absent from the package
    When the replicate-source-tree phase runs
    Then no nodes are created and a warning is logged
