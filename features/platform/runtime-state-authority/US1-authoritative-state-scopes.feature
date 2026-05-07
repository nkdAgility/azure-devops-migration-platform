@platform
Feature: Runtime state authoritative scopes
  Ensure package root and project state drive resume while run scope stays audit-only.

  @runtime-state-us1
  Scenario: Resume_UsesAuthoritativeScopes_RunScopeIgnored
    Given a package contains root and project migration state
    And the run audit folder contains stale copies of those files
    When a migration job evaluates resume and phase gates
    Then only root and project scoped files are used as authoritative state
    And run-scope files remain inspectable audit artefacts only
