Feature: Identity Mapping and Resolution
  As a migration operator
  I want source identities to be mapped to target identities before import
  So that work items in the target reference valid target users

  Background:
    Given the identities export has completed before any import module runs

  Scenario: Source identity is mapped to the correct target identity during import
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
    And a warning is recorded in ".migration/Logs/" for the unmapped identity

  Scenario: No module performs inline identity resolution
    Given any module that writes user references during import
    When the module applies a revision
    Then all identity lookups are handled by the central identity mapping configuration
    And the import module does not contact the identity service directly

  Scenario: Identities export must complete before work items import begins
    Given the identities export has not yet completed
    When the work items import is invoked
    Then the work items import does not begin until the identities export is complete
    And the work items import is configured to require the identities export as a prerequisite
