Feature: Identity Mapping and Resolution
  As a migration operator
  I want source identities to be mapped to target identities before import
  So that work items in the target reference valid target users

  Background:
    Given the IdentitiesModule has completed export before any module that maps identities

  Scenario: Identity is resolved via IIdentityMappingService during import
    Given a revision.json assigns a work item to source user "jsmith@source.example.com"
    And an identity mapping exists from "jsmith@source.example.com" to "john.smith@target.example.com"
    When the WorkItems import module applies the revision
    Then the target work item is assigned to "john.smith@target.example.com"

  Scenario: Unmapped identity falls back to the configured default identity
    Given a revision.json assigns a work item to source user "legacy@old.example.com"
    And no mapping exists for "legacy@old.example.com"
    And the configuration specifies a fallback identity of "migration-bot@target.example.com"
    When the WorkItems import module applies the revision
    Then the target work item is assigned to "migration-bot@target.example.com"
    And a warning is recorded in "Logs/" for the unmapped identity

  Scenario: No module performs inline identity resolution
    Given any module that writes user references during import
    When the module applies a revision
    Then all identity lookups are delegated to IIdentityMappingService
    And no direct Azure DevOps Identities API calls are made within the module

  Scenario: IdentitiesModule must complete before WorkItems import begins
    Given the IdentitiesModule has not yet completed
    When the WorkItems import module is invoked
    Then the WorkItems import module is blocked until IdentitiesModule completes
    And the WorkItems module declares "Identities" in its DependsOn list

  Scenario: Identity export writes known identities to the package
    Given the source project contains users "alice@source.example.com" and "bob@source.example.com"
    When the IdentitiesModule export runs
    Then "Identities/identities.json" is written to the package
    And it contains entries for both "alice@source.example.com" and "bob@source.example.com"
