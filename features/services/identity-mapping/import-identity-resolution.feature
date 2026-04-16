Feature: Identity Resolution During Import
  As a migration operator
  I want source user identities to be mapped to target identities during import
  So that work item history shows the correct people in the target project

  Background:
    Given a valid migration package exists at the configured package root
    And an identity mapping file exists at "Identities/mapping.json"

  @identity-resolution
  Scenario: Mapped identity is resolved from the mapping file
    Given the identity mapping contains a mapping from "user@source.com" to "user@target.com"
    And a revision contains field "System.AssignedTo" with value "user@source.com"
    When the import processes Stage B (AppliedFields) for that revision
    Then the field "System.AssignedTo" is applied to the target with value "user@target.com"

  @identity-resolution
  Scenario: Unmapped identity falls back and is recorded for review
    Given the identity mapping does not contain an entry for "unknown@source.com"
    And a revision contains field "System.ChangedBy" with value "unknown@source.com"
    When the import processes Stage B (AppliedFields) for that revision
    Then the import continues without failure
    And "unknown@source.com" is recorded in "Identities/unresolved.json"

  @identity-resolution
  Scenario: Identity fields are passed through when no mapping is configured
    Given no identity mapping file is configured
    And a revision contains field "System.CreatedBy" with value "someuser@domain.com"
    When the import processes Stage B (AppliedFields) for that revision
    Then the field "System.CreatedBy" is applied to the target unchanged
