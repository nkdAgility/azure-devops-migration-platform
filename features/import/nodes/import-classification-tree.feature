Feature: Import Classification Tree
  As a migration operator
  I want the source classification tree to be recreated on the target during import
  So that work item area and iteration paths are valid on the target

  # BLOCKED: Feature says "NodesModule is configured with AutoCreateNodes = true" but
  # AutoCreateNodes is on NodeTranslationOptions (MigrationPlatform:Tools:NodeTranslation),
  # not NodesModuleOptions. NodesModule.ImportAsync never reads this option.
  # See: src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/NodesModuleOptions.cs
  #      src/DevOpsMigrationPlatform.Abstractions/Options/NodeTranslationOptions.cs
  Scenario: AutoCreateNodes ensures referenced paths exist on target
    Given a package containing "Nodes/referenced-paths.json" with paths ["Project\\Area1", "Project\\Sprint 1"]
    And NodesModule is configured with AutoCreateNodes = true
    When NodesModule ImportAsync runs
    Then INodeEnsurer.EnsureReferencedPathsAsync is invoked

  # BLOCKED: (1) INodeEnsurer does not exist — actual interface is INodesOrchestrator.
  # (2) NodesModule.ImportAsync always calls orchestrator.ImportAsync when Enabled=true;
  # there is no skip-when-both-false guard at the module level.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs:240-260
  Scenario: Import is skipped when both ReplicateSourceTree and AutoCreateNodes are false
    Given NodesModule is configured with ReplicateSourceTree = false and AutoCreateNodes = false
    When NodesModule ImportAsync runs
    Then neither INodeEnsurer method is invoked
