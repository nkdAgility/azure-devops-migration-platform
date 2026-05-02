@platform
Feature: Module Developers Inject Only Their Own Config Slice
  Each module receives only its own options type without accessing the full platform options graph

  Background:
    Given all modules are migrated to isolated config injection

  @module-isolation
  Scenario: ModuleConstructed_IsolatedOptions_NoFullGraph
    Given WorkItemsModule is being instantiated
    When the module constructor is called
    Then the constructor receives IOptions<WorkItemsModuleOptions>
    And the constructor receives IAgentJobContext
    And the constructor receives ISourceEndpointInfo
    And the constructor receives ITargetEndpointInfo
    And the constructor does NOT receive the full platform options graph

  @module-testing
  Scenario: ModuleUnitTest_IsolatedOptions_MinimalDependencies
    Given a unit test for WorkItemsModule
    When the test constructs the module
    Then the test only provides WorkItemsModuleOptions
    And the test provides mock implementations of IAgentJobContext and endpoint info
    And the test does NOT need to construct any other module's options

  @startup-validation
  Scenario: DuplicateSectionName_DIRegistration_FailsAtStartup
    Given two options classes both declare SectionName "Modules:Duplicate"
    When the host starts and attempts to register both types
    Then DI registration throws an exception at startup
    And the error identifies both conflicting type names
    And the error identifies the duplicate SectionName

  @config-contract-explicit
  Scenario: NewModule_FollowsPattern_ExplicitContract
    Given a new module is added following the isolated injection pattern
    When the module is registered in DI
    Then the module's config requirements are explicit in its constructor signature
    And the module's SectionName is defined as a constant on the options type
    And the module can be tested independently of all other modules
