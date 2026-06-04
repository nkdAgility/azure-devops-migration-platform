Feature: Import Classification Tree
  As a migration operator
  I want the source classification tree to be recreated on the target during import
  So that work item area and iteration paths are valid on the target

  # GAP-002/GAP-003 (RESOLVED): the previous scenarios attributed AutoCreateNodes to
  # NodesModuleOptions and asserted against a non-existent INodeEnsurer interface. AutoCreateNodes
  # lives on NodeTranslationOptions (MigrationPlatform:Tools:NodeTranslation) and is owned by the
  # NodeTranslationTool, not NodesModule. Node operations go through INodesOrchestrator.
  # The scenarios below describe the NodesModule skip-guard contract (FR-007). Behaviour is
  # verified by NodesModuleTests (unit) — these steps are documentation of the module contract.

  Scenario: Import is skipped when ReplicateSourceTree is false
    Given NodesModule is configured with Enabled = true and ReplicateSourceTree = false
    When NodesModule ImportAsync runs
    Then INodesOrchestrator.ImportAsync is not called
    And the result is Skipped

  Scenario: Import is skipped when the module is disabled
    Given NodesModule is configured with Enabled = false
    When NodesModule ImportAsync runs
    Then INodesOrchestrator.ImportAsync is not called
    And the result is Skipped

  Scenario: Classification tree is imported when ReplicateSourceTree is true
    Given NodesModule is configured with Enabled = true and ReplicateSourceTree = true
    When NodesModule ImportAsync runs
    Then INodesOrchestrator.ImportAsync is called
