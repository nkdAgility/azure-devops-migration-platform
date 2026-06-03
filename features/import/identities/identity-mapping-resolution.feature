Feature: Identity Mapping Resolution
  As a migration operator
  I want source identities to be resolved to target identities
  So that work items and teams reference valid target identities after import

  # BLOCKED: UPN matching is documented in IdentityMappingService docstring (resolution step 2)
  # but not implemented — Resolve() skips directly to FallbackIdentity() after explicit-override check.
  # Expected outcome (bob@source.com → bob@target.com) cannot be confirmed against production behaviour.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityMappingService.cs:21-88
  Scenario: Automatic UPN match resolves identity
    Given a source identity "bob@source.com" with display name "Bob Smith"
    And the mapping.json file has no override for "bob@source.com"
    When the identity is resolved
    Then the resolved identity is "bob@target.com"
