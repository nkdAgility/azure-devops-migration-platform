Feature: ADO Boundary Isolation for Simulated Import
  As a platform architect
  I want the Azure DevOps assembly to have zero knowledge of the Simulated connector
  So that connectors are properly isolated and ADO can be replaced without touching Simulated code

  @unit
  Scenario: ADO import target factory rejects SimulatedEndpointOptions
    Given the AzureDevOpsWorkItemImportTargetFactory is configured
    When CreateAsync is called with a SimulatedEndpointOptions endpoint
    Then an ArgumentException is thrown
    And the exception message references the unexpected endpoint type

  @unit
  Scenario: ADO import target factory accepts AzureDevOpsEndpointOptions
    Given the AzureDevOpsWorkItemImportTargetFactory is configured
    When CreateAsync is called with a valid AzureDevOpsEndpointOptions endpoint
    Then an AzureDevOpsWorkItemImportTarget is returned without error

  @unit
  Scenario: ADO resolution strategy factory does not match SimulatedWorkItemImportTarget
    Given the AzureDevOpsResolutionStrategyFactory is configured
    When CanHandle is evaluated for a SimulatedWorkItemImportTarget
    Then the factory does not handle that target type
    And routing falls through to the Simulated resolution strategy factory
