Feature: Identity Mapping Resolution
  As a migration operator
  I want source identities to be resolved to target identities
  So that work items and teams reference valid target identities after import

  Scenario: Explicit mapping override takes precedence
    Given a source identity "alice@source.com"
    And the mapping.json file contains an override mapping "alice@source.com" to "alice@target.com"
    When the identity is resolved
    Then the resolved identity is "alice@target.com"

  Scenario: Automatic UPN match resolves identity
    Given a source identity "bob@source.com" with display name "Bob Smith"
    And the mapping.json file has no override for "bob@source.com"
    When the identity is resolved
    Then the resolved identity is "bob@target.com"

  Scenario: Unresolvable identity falls back to configured default
    Given a source identity "unknown@source.com"
    And the mapping.json file has no override for "unknown@source.com"
    And the default identity is configured as "admin@target.com"
    When the identity is resolved
    Then the resolved identity is "admin@target.com"

  Scenario: Missing descriptors.jsonl causes validation failure
    Given the package does not contain "Identities/descriptors.jsonl"
    When ValidateAsync is invoked on the IdentitiesModule
    Then validation fails with a missing file error for "Identities/descriptors.jsonl"

  Scenario: Malformed JSONL causes validation failure
    Given the package contains "Identities/descriptors.jsonl" with non-JSON content
    When ValidateAsync is invoked on the IdentitiesModule
    Then validation fails with a parse error

  Scenario: Identity export writes descriptors file to package
    Given the source project contains identities "alice@source.com" and "bob@source.com"
    When the IdentitiesModule ExportAsync runs
    Then "Identities/descriptors.jsonl" is written to the package
    And it contains entries for "alice@source.com" and "bob@source.com"
