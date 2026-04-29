Feature: Import Classification Tree
  As a migration operator
  I want the source classification tree to be recreated on the target during import
  So that work item area and iteration paths are valid on the target

  Scenario: Replicate source tree creates all nodes on target when ReplicateSourceTree is true
    Given a package containing "Nodes/source-tree.json" with area "Project\\Area1" and iteration "Project\\Sprint 1"
    And NodesModule is configured with ReplicateSourceTree = true
    When NodesModule ImportAsync runs
    Then INodeEnsurer.ReplicateSourceTreeAsync is invoked

  Scenario: AutoCreateNodes ensures referenced paths exist on target
    Given a package containing "Nodes/referenced-paths.json" with paths ["Project\\Area1", "Project\\Sprint 1"]
    And NodesModule is configured with AutoCreateNodes = true
    When NodesModule ImportAsync runs
    Then INodeEnsurer.EnsureReferencedPathsAsync is invoked

  Scenario: Import is skipped when both ReplicateSourceTree and AutoCreateNodes are false
    Given NodesModule is configured with ReplicateSourceTree = false and AutoCreateNodes = false
    When NodesModule ImportAsync runs
    Then neither INodeEnsurer method is invoked

  Scenario: ValidateAsync reports error when source-tree.json is missing
    Given the package does not contain "Nodes/source-tree.json"
    When NodesModule ValidateAsync is invoked
    Then validation fails with a missing file error for "Nodes/source-tree.json"

  Scenario: ValidateAsync passes when source-tree.json is present and valid
    Given the package contains valid "Nodes/source-tree.json"
    When NodesModule ValidateAsync is invoked
    Then validation passes with no errors
