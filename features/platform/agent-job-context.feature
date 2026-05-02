@platform
Feature: Cross-Cutting Job Context Available Without Monolithic Options
  Modules read Mode, PackagePath, ConfigVersion from IAgentJobContext without accessing the full options graph

  Background:
    Given IAgentJobContext is registered in DI

  @module-isolation
  Scenario: ModuleReadsMode_ContextProvided_NoFullOptionsGraph
    Given a migration job with Mode "Export"
    And IAgentJobContext is resolved from DI
    When a module reads IAgentJobContext.Mode
    Then the module receives "Export"
    And the module does not have access to any other module's config

  @module-isolation
  Scenario: ModuleReadsPackagePath_ContextProvided_NoFullOptionsGraph
    Given a migration job with PackagePath "C:\exports\run-001"
    And IAgentJobContext is resolved from DI
    When a module reads IAgentJobContext.PackagePath
    Then the module receives "C:\exports\run-001"
    And the module does not have access to target connector config

  @context-read-only
  Scenario: ContextIsReadOnly_ModuleAccesses_NoWritePath
    Given IAgentJobContext is registered as a read-only interface
    When a module resolves IAgentJobContext from DI
    Then the module can read Mode, PackagePath, and ConfigVersion
    And no module can write to the context
    And no module can observe another module's side effects through it

  @tfs-source-only
  Scenario: TfsSourceOnlyJob_ContextResolved_NoTargetInfo
    Given a migration job with Mode "Export"
    And the source connector is "TeamFoundationServer"
    And no target endpoint is configured
    When IAgentJobContext is resolved from DI
    Then the context resolves successfully with Mode and PackagePath
    And ITargetEndpointInfo is not required for resolution
